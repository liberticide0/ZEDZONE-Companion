using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Companion;

internal sealed record UpdateRelease(Version Version, Uri Download, long Size, string Sha256)
{
    private const string Repo = "liberticide0/ZEDZONE-Companion";
    private const long MaxDownload = 256L * 1024 * 1024;
    private static readonly Uri Latest = new($"https://api.github.com/repos/{Repo}/releases/latest");
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
        MaxResponseContentBufferSize = 1024 * 1024
    };

    internal static async Task<UpdateRelease?> CheckAsync(Version installed, CancellationToken cancellation,
        HttpClient? client = null)
    {
        using var request = Request(Latest);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await (client ?? Http).SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation),
            cancellationToken: cancellation);
        return Read(json.RootElement, installed);
    }

    internal static UpdateRelease? Read(JsonElement release, Version installed)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            return null;
        string? tag = release.GetProperty("tag_name").GetString();
        if (tag is not { Length: > 1 } || tag[0] != 'v' ||
            !Version.TryParse(tag[1..], out var version) || version.Build < 0 || version.Revision != -1 ||
            tag != $"v{version}" || version <= installed)
            return null;

        string name = $"ZEDZONE-Companion-{version}-win-x64.zip";
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name)
                continue;
            long size = asset.GetProperty("size").GetInt64();
            string url = $"https://github.com/{Repo}/releases/download/{tag}/{name}";
            if (asset.GetProperty("state").GetString() != "uploaded" || size is <= 0 or > MaxDownload ||
                asset.GetProperty("browser_download_url").GetString() != url ||
                !asset.TryGetProperty("digest", out var digest) ||
                digest.GetString() is not { Length: 71 } hash || !hash.StartsWith("sha256:", StringComparison.Ordinal) ||
                !hash.AsSpan(7).ToString().All(Uri.IsHexDigit))
                throw new InvalidDataException("공식 Windows x64 패키지의 해시 또는 정보가 올바르지 않습니다.");
            return new UpdateRelease(version, new Uri(url), size, hash[7..]);
        }
        throw new InvalidDataException("정식 릴리스에서 Windows x64 패키지를 찾을 수 없습니다.");
    }

    internal async Task DownloadAsync(string file, CancellationToken cancellation, HttpClient? client = null)
    {
        Uri url = Download;
        for (int redirects = 0; redirects <= 4; redirects++)
        {
            if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort ||
                url.UserInfo.Length != 0 || url.Host is not
                ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("공식 배포처의 다운로드 주소를 확인할 수 없습니다.");
            using var request = Request(url);
            using var response = await (client ?? Http).SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellation);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } next)
            {
                url = new Uri(url, next);
                continue;
            }
            response.EnsureSuccessStatusCode();
            using var input = await response.Content.ReadAsStreamAsync(cancellation);
            using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[128 * 1024];
            long length = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
            {
                length += read;
                if (length > Size)
                    throw new InvalidDataException("다운로드 크기가 릴리스 정보와 다릅니다.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
            }
            if (length != Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("다운로드한 패키지의 SHA-256이 일치하지 않습니다.");
            return;
        }
        throw new InvalidDataException("다운로드 주소 이동이 너무 많습니다.");
    }

    private static HttpRequestMessage Request(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ZEDZONE-Companion-Updater/1");
        return request;
    }
}
