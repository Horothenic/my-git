using Avalonia.Media.Imaging;

namespace MyGit.App.Services;

/// <summary>
/// Downloads small profile pictures for display.
/// </summary>
public static class AvatarLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Returns the image, or null if it couldn't be downloaded.</summary>
    public static async Task<Bitmap?> LoadAsync(string url, int size = 64)
    {
        try
        {
            // GitHub avatar URLs accept a size parameter, which keeps the download tiny.
            var separator = url.Contains('?') ? '&' : '?';
            await using var stream = await Http.GetStreamAsync($"{url}{separator}s={size}");

            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            memory.Position = 0;

            return new Bitmap(memory);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or ArgumentException)
        {
            return null;
        }
    }
}
