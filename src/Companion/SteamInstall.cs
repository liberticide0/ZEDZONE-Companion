using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Companion;

internal static class SteamInstall
{
    private const string AppId = "1211600";
    private static readonly Regex Tokens = new(
        """//[^\r\n]*|"(?:\\.|[^"\\])*"|[{}]""", RegexOptions.CultureInvariant);
    private static readonly Regex Escapes = new("""\\(["\\])""", RegexOptions.CultureInvariant);

    internal static IEnumerable<string> LocalRoots()
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        foreach (var (hive, subkey, value) in new[]
        {
            (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath"),
            (Registry.LocalMachine, @"Software\Valve\Steam", "InstallPath")
        })
        {
            string? root;
            try
            {
                using var key = hive.OpenSubKey(subkey);
                root = key?.GetValue(value) as string;
            }
            catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(root))
                yield return root.Replace('/', Path.DirectorySeparatorChar);
        }

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (programFiles.Length > 0)
            yield return Path.Combine(programFiles, "Steam");
    }

    internal static IEnumerable<(string Path, string Build, string GameDirectory)> FindLocalization(IEnumerable<string> steamRoots)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in steamRoots)
        {
            foreach (string library in LibraryPaths(root))
            {
                if (!visited.Add(library))
                    continue;

                var manifest = ReadPairs(Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf"));
                string? id = Value(manifest, "appid");
                string? folder = Value(manifest, "installdir");
                string? build = Value(manifest, "buildid");
                if (id != AppId || string.IsNullOrWhiteSpace(folder) || folder is "." or ".." ||
                    folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    folder.Contains('/') || folder.Contains('\\') || folder.Contains(':'))
                    continue;

                string game = Path.Combine(library, "steamapps", "common", folder);
                string csv = Path.Combine(game, "ZEDZONE_Data",
                    "StreamingAssets", "Localization", "Localization_en_to_ko.csv");
                if (File.Exists(csv))
                    yield return (csv, build is { Length: > 0 and <= 20 } && build.All(char.IsAsciiDigit) ? build : "", game);
            }
        }
    }

    private static IEnumerable<string> LibraryPaths(string root)
    {
        if (!IsLocalPath(root))
            yield break;

        yield return root;
        var pairs = ReadPairs(Path.Combine(root, "steamapps", "libraryfolders.vdf"));
        foreach (var (key, path, depth) in pairs)
        {
            bool libraryPath = depth == 2 && key.Equals("path", StringComparison.OrdinalIgnoreCase);
            bool legacyPath = depth == 1 && key.All(char.IsAsciiDigit);
            if ((libraryPath || legacyPath) && IsLocalPath(path))
                yield return path;
        }
    }

    private static bool IsLocalPath(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            return false;
        if (!OperatingSystem.IsWindows())
            return true;

        string? drive = Path.GetPathRoot(path);
        if (drive is not { Length: 3 } || drive[1] != ':' || !char.IsAsciiLetter(drive[0]))
            return false;

        try
        {
            return new DriveInfo(drive).DriveType != DriveType.Network;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? Value(List<(string Key, string Text, int Depth)> pairs, string key) =>
        pairs.Find(pair => pair.Depth == 1 && pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Text;

    private static List<(string Key, string Text, int Depth)> ReadPairs(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024)
                return [];

            var pairs = new List<(string, string, int)>();
            int depth = 0;
            string? key = null;
            foreach (Match match in Tokens.Matches(File.ReadAllText(path)))
            {
                string token = match.Value;
                if (token[0] == '/')
                    continue;
                if (token[0] != '"')
                {
                    depth += token[0] == '{' ? 1 : -1;
                    if (depth < 0)
                        return [];
                    key = null;
                    continue;
                }

                string value = Escapes.Replace(token[1..^1], "$1");
                if (key is null)
                    key = value;
                else
                {
                    pairs.Add((key, value, depth));
                    key = null;
                }
            }
            return depth == 0 ? pairs : [];
        }
        catch (Exception error) when (Catalog.IsImportError(error))
        {
            return [];
        }
    }
}
