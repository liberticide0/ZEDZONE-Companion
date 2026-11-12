namespace Companion;

internal static class ItemIconCache
{
    internal static string DirectoryFor(string build) => Path.Combine(UpdateFixture.DataPath, $"icons-v1-{build}");

    internal static Bitmap? Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > 1024 * 1024)
                return null;
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            return image.Width <= 1024 && image.Height <= 1024 ? new Bitmap(image) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }
}
