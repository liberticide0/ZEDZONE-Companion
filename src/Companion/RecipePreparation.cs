using System.Diagnostics;

namespace Companion;

internal static class RecipePreparation
{
    private static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "converter");
    private static string PythonPath => Path.Combine(DirectoryPath, "python", "python.exe");
    private static string ScriptPath => Path.Combine(DirectoryPath, "converter-bootstrap.py");
    internal const int PackagesRequired = 10;

    public static bool IsAvailable => File.Exists(PythonPath) && File.Exists(ScriptPath) &&
        File.Exists(Path.Combine(DirectoryPath, "converter-packages.json"));

    public static async Task<int> RunAsync(string game, bool allowDownload, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo
        {
            FileName = PythonPath, WorkingDirectory = DirectoryPath, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
#if UPDATE_FIXTURE
        start.Environment["LOCALAPPDATA"] = UpdateFixture.DataParent;
#endif
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add("-X");
        start.ArgumentList.Add("utf8");
        start.ArgumentList.Add(ScriptPath);
        start.ArgumentList.Add(game);
        if (allowDownload)
            start.ArgumentList.Add("--allow-download");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("변환기를 시작하지 못했습니다.");
        Task output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        Task errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, errors);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the state check and termination.
            }
            cancellation.ThrowIfCancellationRequested();
            throw new TimeoutException("제작 자료 준비 시간이 초과되었습니다.");
        }
    }
}
