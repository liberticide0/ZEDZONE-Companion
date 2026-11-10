using System.Diagnostics;

namespace Companion;

internal static class UpdateWorker
{
    private const string TempPrefix = "ZEDZONE-Companion-update-";
    private const string Helper = "Companion-update.exe";
    private const string RestartNotice = ".companion-restart-needed";
    internal static string InstallPath => Path.GetDirectoryName(Environment.ProcessPath!)!;
    internal static bool IsPortable =>
        string.Equals(Path.GetFileName(Environment.ProcessPath), UpdatePackage.Exe, StringComparison.OrdinalIgnoreCase) &&
        RecipePreparation.IsAvailable;

    internal static async Task HandOffAsync(UpdateRelease release, CancellationToken cancellation)
    {
        string temporary = TempPath(Guid.NewGuid());
        Directory.CreateDirectory(temporary);
        var package = new UpdatePackage(InstallPath);
        bool prepared = false;
        string archive = Path.Combine(temporary, "release.zip");
        try
        {
            // Check folder access before downloading anything.
            package.CheckWritable();
            await release.DownloadAsync(archive, cancellation);
            await Task.Run(() => package.Prepare(archive, release.Version, cancellation), cancellation);
            prepared = true;
            File.Delete(archive);
            string helper = Path.Combine(temporary, Helper);
            File.Copy(Environment.ProcessPath!, helper);
            cancellation.ThrowIfCancellationRequested();
            Start(helper, "--apply-update", InstallPath, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
            if (prepared)
                package.Recover();
            TryCleanup(temporary);
            throw;
        }
    }

    internal static void ApplyAndRestart(string install, int parentId, Mutex instance)
    {
        string directory = Path.GetFileName(InstallPath);
        if (!Path.IsPathFullyQualified(install) || parentId <= 0 ||
            !directory.StartsWith(TempPrefix, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(directory[TempPrefix.Length..], "N", out var token) ||
            !string.Equals(InstallPath, TempPath(token), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(Environment.ProcessPath), Helper, StringComparison.OrdinalIgnoreCase))
            return;
        string exe = Path.Combine(install, UpdatePackage.Exe);
        try
        {
            using var parent = Process.GetProcessById(parentId);
            if (!string.Equals(parent.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase) ||
                !parent.WaitForExit(30_000))
                return;
        }
        catch (ArgumentException) { } // The parent already exited.
        catch (InvalidOperationException) { } // The parent exited while its image was being read.
        catch (System.ComponentModel.Win32Exception) { return; }

        if (!Program.Acquire(instance, TimeSpan.FromSeconds(30)))
            return;
        try
        {
            string result = "ok";
            try { new UpdatePackage(install).Apply(); }
            catch (Exception error) when (IsUpdateError(error)) { result = "failed"; }
            try { Start(exe, "--finish-update", token.ToString("N"), result); }
            catch (Exception error) when (IsUpdateError(error))
            {
                try
                {
                    new UpdatePackage(install).CheckWritable();
                    using var notice = new FileStream(Path.Combine(install, RestartNotice), FileMode.CreateNew);
                }
                catch (Exception noticeError) when (IsUpdateError(noticeError)) { }
            }
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }

    internal static bool ConsumeRestartNotice()
    {
        string path = Path.Combine(InstallPath, RestartNotice);
        if (!File.Exists(path))
            return false;
        try
        {
            new UpdatePackage(InstallPath).CheckWritable();
            File.Delete(path);
        }
        catch (Exception error) when (IsUpdateError(error)) { }
        return true;
    }

    internal static void CleanupLater(Guid token) => _ = Task.Run(async () =>
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (TryCleanup(TempPath(token)))
                return;
            await Task.Delay(400);
        }
    });

    internal static bool IsUpdateError(Exception error) =>
        error is IOException or UnauthorizedAccessException or InvalidDataException or BadImageFormatException or
            HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or
            KeyNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception or FormatException;

    private static string TempPath(Guid token) => Path.Combine(Path.GetTempPath(), TempPrefix + token.ToString("N"));

    private static bool TryCleanup(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return true;
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                return false;
            File.Delete(Path.Combine(directory, Helper));
            File.Delete(Path.Combine(directory, "release.zip"));
            Directory.Delete(directory);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void Start(string exe, params string[] args)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (string argument in args)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("업데이트 프로세스를 실행할 수 없습니다.");
    }
}
