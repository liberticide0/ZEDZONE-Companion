using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Companion;

internal static class UpdateSmoke
{
    private static readonly Version Installed = new(0, 4, 6);
    private static readonly Version Next = new(0, 4, 7);
    private const string Asset = "ZEDZONE-Companion-0.4.7-win-x64.zip";
    private const string Url = "https://github.com/liberticide0/ZEDZONE-Companion/releases/download/v0.4.7/" + Asset;

    public static async Task Run(Action<bool, string> check)
    {
        string root = NewFolder();
        try
        {
            byte[] bytes = [4, 3, 2, 1];
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            using var metadata = Feed(hash, bytes.Length, bytes);
            var release = (await UpdateRelease.CheckAsync(Installed, default, metadata))!;
            check(release.Version == Next && release.Download.AbsoluteUri == Url, "offer a newer official win-x64 release");
            check(await UpdateRelease.CheckAsync(Next, default, metadata) is null, "same version is not offered");
            using var preview = Feed(hash, bytes.Length, bytes, prerelease: true);
            check(await UpdateRelease.CheckAsync(Installed, default, preview) is null, "pre-release is ignored");
            using var draft = Feed(hash, bytes.Length, bytes, draft: true);
            check(await UpdateRelease.CheckAsync(Installed, default, draft) is null, "draft is ignored");
            using var none = new HttpClient(new Reply((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
            check(await UpdateRelease.CheckAsync(Installed, default, none) is null, "no stable release is a normal result");
            using var offline = new HttpClient(new Reply((_, _) => throw new HttpRequestException("offline fixture")));
            await Reject<HttpRequestException>(() => UpdateRelease.CheckAsync(Installed, default, offline));
            check(true, "network failure is reported without an update");
            using var missing = Feed("", bytes.Length, bytes);
            await Reject<InvalidDataException>(() => UpdateRelease.CheckAsync(Installed, default, missing));
            check(true, "release without a valid SHA-256 is rejected");
            await release.DownloadAsync(Path.Combine(root, "good"), default, metadata);
            check(File.ReadAllBytes(Path.Combine(root, "good")).SequenceEqual(bytes), "valid official asset is downloaded");
            using var bad = Feed(new string('0', 64), bytes.Length, bytes);
            var wrong = (await UpdateRelease.CheckAsync(Installed, default, bad))!;
            await Reject<InvalidDataException>(() => wrong.DownloadAsync(Path.Combine(root, "bad"), default, bad));
            check(true, "SHA-256 mismatch is rejected");
            using var redirect = new HttpClient(new Reply((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("https://example.com/package.zip") }
            })));
            await Reject<InvalidDataException>(() => release.DownloadAsync(Path.Combine(root, "redirect"), default, redirect));
            check(true, "asset redirects outside GitHub are rejected");

            foreach (string path in new[] { "../outside.txt", "/absolute.txt", "converter/../escape", "converter/CON.txt",
                "converter/stream:content", @"converter\backslash", "unmanaged.exe" })
            {
                string archive = Path.Combine(root, "unsafe.zip");
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                    using (var writer = new StreamWriter(zip.CreateEntry(path).Open()))
                        writer.Write("unsafe");
                await Reject<InvalidDataException>(() =>
                {
                    UpdatePackage.Extract(archive, Path.Combine(root, "extract"), default);
                    return Task.CompletedTask;
                });
                File.Delete(archive);
            }
            check(!Directory.Exists(Path.Combine(root, "extract")), "unsafe ZIP paths do not create files");
            if (OperatingSystem.IsWindows())
            {
                var protectedFolder = Directory.CreateDirectory(Path.Combine(root, "read only"));
                using var identity = WindowsIdentity.GetCurrent();
                var original = protectedFolder.GetAccessControl();
                var denied = protectedFolder.GetAccessControl();
                denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.CreateFiles,
                    AccessControlType.Deny));
                protectedFolder.SetAccessControl(denied);
                try
                {
                    await Reject<UnauthorizedAccessException>(() =>
                    {
                        new UpdatePackage(protectedFolder.FullName).CheckWritable();
                        return Task.CompletedTask;
                    });
                    check(!protectedFolder.EnumerateFileSystemInfos().Any(), "write-protected installation is detected before download");
                }
                finally { protectedFolder.SetAccessControl(original); }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    public static async Task Integration(string oldZip, string newZip, Action<bool, string> check)
    {
        string root = NewFolder();
        string installed = Path.Combine(root, "installed app");
        string helperDir = Path.Combine(Path.GetTempPath(), $"ZEDZONE-Companion-update-{Guid.NewGuid():N}");
        string app = Path.Combine(installed, UpdatePackage.Exe);
        string journal = Path.Combine(installed, ".companion-update");
        Process? parent = null;
        Process? worker = null;
        Process? restarted = null;
        try
        {
            if (Process.GetProcessesByName("ZEDZONE.Companion").Length != 0)
                throw new InvalidOperationException("Close Companion before the isolated process integration check.");
            ZipFile.ExtractToDirectory(oldZip, installed);
            string oldHash = Hash(app);
            string preferences = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZEDZONE.Companion");
            var userFiles = Directory.Exists(preferences) ? Directory.GetFiles(preferences)
                .Where(file => Path.GetFileName(file) == "wishlist.json" ||
                    Path.GetFileName(file).StartsWith("recipes-", StringComparison.Ordinal)).ToArray() : [];
            var userHashes = userFiles.ToDictionary(file => file, Hash);
            string personal = Path.Combine(installed, "unrelated.txt");
            File.WriteAllText(personal, "retain");
            string installedReadme = Path.Combine(installed, "README.md");
            File.AppendAllText(installedReadme, "\nold fixture");
            string oldReadme = Hash(installedReadme);
            byte[] bytes = File.ReadAllBytes(newZip);
            using var fixture = Feed(Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, bytes);
            var release = (await UpdateRelease.CheckAsync(Installed, default, fixture))!;
            string downloaded = Path.Combine(root, "release.zip");
            await release.DownloadAsync(downloaded, default, fixture);
            var package = new UpdatePackage(installed);
            await Reject<InvalidDataException>(() =>
            {
                package.Prepare(downloaded, Installed, default);
                return Task.CompletedTask;
            });
            check(!Directory.Exists(journal) && Hash(app) == oldHash, "tag and executable version mismatch leaves installation unchanged");
            package.Prepare(downloaded, release.Version, default);
            File.WriteAllText(Path.Combine(journal, "ready"), "");
            File.Move(installedReadme, Path.Combine(journal, "old", "README.md"));
            File.Move(Path.Combine(journal, "new", "README.md"), installedReadme);
            package.Recover();
            check(Hash(installedReadme) == oldReadme && Hash(app) == oldHash && !Directory.Exists(journal),
                "startup recovery rolls back an interrupted replacement");

            package.Prepare(downloaded, release.Version, default);
            using (var locked = new FileStream(Path.Combine(installed, "LICENSE"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await Reject<IOException>(() =>
                {
                    package.Apply();
                    return Task.CompletedTask;
                });
            }
            check(Hash(installedReadme) == oldReadme && Hash(app) == oldHash && !Directory.Exists(journal),
                "locked managed file rolls back the prior move");

            package.Prepare(downloaded, release.Version, default);
            parent = Start(app);
            await WaitFor(() => { parent.Refresh(); return parent.MainWindowHandle != 0; });
            Directory.CreateDirectory(helperDir);
            string helper = Path.Combine(helperDir, "Companion-update.exe");
            File.Copy(app, helper);
            worker = Start(helper, "--apply-update", installed, parent.Id.ToString());
            await Task.Delay(800);
            check(!worker.HasExited && Hash(app) == oldHash, "separate update process waits for the running app");
            parent.Kill();
            await parent.WaitForExitAsync();
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
            await WaitFor(() =>
            {
                foreach (var process in Process.GetProcessesByName("ZEDZONE.Companion"))
                {
                    if (process.MainModule?.FileName == app && process.MainWindowHandle != 0)
                    {
                        restarted = process;
                        return true;
                    }
                    process.Dispose();
                }
                return false;
            });
            check(FileVersionInfo.GetVersionInfo(app).ProductVersion?.Split('+')[0] == Next.ToString() &&
                restarted is not null && Hash(app) != oldHash, "worker installs the new version and relaunches at the same path");
            check(!Directory.Exists(journal) && File.ReadAllText(personal) == "retain" &&
                userHashes.All(pair => Hash(pair.Key) == pair.Value), "user files and unrelated installation files are preserved");
        }
        finally
        {
            foreach (var process in new[] { parent, worker, restarted })
            {
                if (process is null) continue;
                if (!process.HasExited)
                {
                    process.Kill();
                    await process.WaitForExitAsync();
                }
                process.Dispose();
            }
            if (Directory.Exists(helperDir)) Directory.Delete(helperDir, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static Process Start(string exe, params string[] args)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            if (condition()) return;
            await Task.Delay(250);
        }
        throw new TimeoutException("The isolated app did not start.");
    }

    private static string NewFolder() => Directory.CreateTempSubdirectory("CompanionUpdateSmoke-").FullName;

    private static async Task Reject<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static HttpClient Feed(string hash, int length, byte[] bytes, bool prerelease = false, bool draft = false) =>
        new(new Reply((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.Host == "api.github.com"
                ? new StringContent(JsonSerializer.Serialize(new { draft, prerelease, tag_name = "v0.4.7", assets = new[]
                    { new { name = Asset, size = length, state = "uploaded", browser_download_url = Url, digest = "sha256:" + hash } } }))
                : new ByteArrayContent(bytes)
        })));

    private sealed class Reply(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            reply(request, cancellationToken);
    }
}
