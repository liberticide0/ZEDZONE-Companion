using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Companion;

internal sealed class UpdatePackage(string install)
{
    internal const string Exe = "ZEDZONE.Companion.exe";
    private static readonly string[] Components = ["README.md", "LICENSE", "probe-recipes.py", "dotnet-notices", "converter"];
    private static readonly string[] Required =
    [
        Exe, "README.md", "LICENSE", "probe-recipes.py",
        "converter/converter-bootstrap.py", "converter/converter-packages.json",
        "converter/python/python.exe", "converter/python/LICENSE.txt",
        "dotnet-notices/microsoft.netcore.app.runtime.win-x64-THIRD-PARTY-NOTICES.TXT",
        "dotnet-notices/microsoft.windowsdesktop.app.runtime.win-x64-THIRD-PARTY-NOTICES.TXT"
    ];
    private static readonly Regex Segment = new(@"\A[A-Za-z0-9_-][A-Za-z0-9_.-]*\z", RegexOptions.CultureInvariant);
    private static readonly Regex Device = new(@"\A(?:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|\z)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private string Root => Path.Combine(install, ".companion-update");
    private string Stage => Path.Combine(Root, "new");
    private string Backup => Path.Combine(Root, "old");
    private string Owner => Path.Combine(Root, "companion-update-v1");
    private string Ready => Path.Combine(Root, "ready");
    private string StagedExe => Path.Combine(Stage, Exe);

    internal void Prepare(string archive, Version version, CancellationToken cancellation)
    {
        CheckInstall();
        if (Exists(Root))
            throw new IOException("이 폴더에 이전 업데이트 자료가 남아 있습니다.");
        Directory.CreateDirectory(Root);
        try
        {
            using (File.Create(Owner)) { }
            Directory.CreateDirectory(Stage);
            Directory.CreateDirectory(Backup);
            Extract(archive, Stage, cancellation);
            using var exe = File.OpenRead(StagedExe);
            using var pe = new PEReader(exe);
            string? product = FileVersionInfo.GetVersionInfo(StagedExe).ProductVersion?.Split('+', 2)[0];
            if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64 || product != version.ToString())
                throw new InvalidDataException("패키지 실행 파일의 버전 또는 Windows 아키텍처가 릴리스와 다릅니다.");
        }
        catch
        {
            Retire();
            throw;
        }
    }

    internal void Apply()
    {
        CheckInstall();
        CheckTree(Root);
        if (!File.Exists(Owner) || !File.Exists(StagedExe) || File.Exists(Ready))
            throw new InvalidDataException("설치할 업데이트 자료가 준비되지 않았습니다.");
        using (File.Create(Ready)) { }
        try
        {
            foreach (string component in Components)
            {
                string live = Path.Combine(install, component);
                if (Exists(live))
                    Move(live, Path.Combine(Backup, component));
                Move(Path.Combine(Stage, component), live);
            }
            // The executable stays in place until the last atomic replacement.
            File.Move(StagedExe, Path.Combine(install, Exe), overwrite: true);
        }
        catch
        {
            Recover();
            throw;
        }
        try { Retire(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    // Called under the application mutex. The current executable is never changed during recovery.
    internal void Recover()
    {
        if (!Exists(Root))
            return;
        CheckTree(Root);
        if (!File.Exists(Owner))
            throw new InvalidDataException("업데이트 임시 폴더의 소유 정보를 확인할 수 없습니다.");
        if (File.Exists(Ready) && File.Exists(StagedExe))
        {
            CheckInstall();
            foreach (string component in Components.Reverse())
            {
                string live = Path.Combine(install, component);
                string old = Path.Combine(Backup, component);
                string staged = Path.Combine(Stage, component);
                if (!Exists(staged) && Exists(live))
                    Move(live, staged);
                if (Exists(old))
                    Move(old, live);
            }
        }
        Retire();
    }

    private void CheckInstall()
    {
        CheckTree(install, descend: false);
        if (!File.Exists(Path.Combine(install, Exe)))
            throw new FileNotFoundException("현재 Companion 실행 파일을 찾을 수 없습니다.");
        CheckTree(Path.Combine(install, Exe));
        foreach (string component in Components)
            CheckTree(Path.Combine(install, component));
    }

    private void Retire()
    {
        // Detach the journal before cleanup so interruption cannot look like a committed transaction.
        string retired = Root + "-done-" + Guid.NewGuid().ToString("N");
        Directory.Move(Root, retired);
        try { Directory.Delete(retired, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static void CheckTree(string path, bool descend = true)
    {
        if (!Exists(path))
            return;
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("설치 폴더에서 연결된 파일 또는 폴더를 발견했습니다.");
        if (descend && attributes.HasFlag(FileAttributes.Directory))
            foreach (string child in Directory.EnumerateFileSystemEntries(path))
                CheckTree(child);
    }

    private static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    private static void Move(string from, string to)
    {
        if (Directory.Exists(from))
            Directory.Move(from, to);
        else
            File.Move(from, to);
    }

    internal static void Extract(string archive, string target, CancellationToken cancellation)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count is < 1 or > 512)
            throw new InvalidDataException("ZIP 파일 개수가 올바르지 않습니다.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.Ordinal);
        long remaining = 512L * 1024 * 1024;
        byte[] buffer = new byte[128 * 1024];
        foreach (var entry in zip.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            bool directory = entry.FullName.EndsWith('/');
            string path = directory ? entry.FullName[..^1] : entry.FullName;
            string[] parts = path.Split('/');
            int unixType = (entry.ExternalAttributes >> 28) & 15;
            if (path.Length > 180 || parts.Any(part => !Segment.IsMatch(part) || part.EndsWith('.') || Device.IsMatch(part)) ||
                !seen.Add(path) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                unixType != 0 && unixType != (directory ? 4 : 8) ||
                !Allowed(parts, directory) || entry.Length < 0 || entry.Length > remaining ||
                directory && entry.Length != 0)
                throw new InvalidDataException("ZIP에 허용되지 않는 경로 또는 파일이 있습니다.");
            string output = Path.Combine(target, Path.Combine(parts));
            if (directory)
            {
                Directory.CreateDirectory(output);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var input = entry.Open();
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            long written = 0;
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                written += read;
                if (written > entry.Length)
                    throw new InvalidDataException("ZIP 항목 크기가 올바르지 않습니다.");
                file.Write(buffer, 0, read);
            }
            if (written != entry.Length)
                throw new InvalidDataException("ZIP 항목이 손상됐습니다.");
            remaining -= written;
            if (written > 0)
                files.Add(path);
        }
        if (!Required.All(files.Contains))
            throw new InvalidDataException("ZIP에 실행 파일, 변환기 또는 배포 고지가 빠졌습니다.");
    }

    private static bool Allowed(string[] parts, bool directory)
    {
        if (parts[0] is "converter" or "dotnet-notices")
            return parts.Length > 1 || directory;
        return !directory && parts.Length == 1 && (parts[0] == Exe || Components.Contains(parts[0]));
    }
}
