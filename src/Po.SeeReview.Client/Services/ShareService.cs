using Microsoft.JSInterop;

namespace Po.SeeReview.Client.Services;

/// <summary>
/// Service for sharing comics on social media using the Web Share API
/// Falls back to clipboard copy for browsers that don't support Web Share API
/// </summary>
public class ShareService
{
    private readonly IJSRuntime _jsRuntime;

    public ShareService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
    }

    /// <summary>
    /// Share a comic using the Web Share API
    /// </summary>
    /// <param name="title">Title of the comic (restaurant name)</param>
    /// <param name="text">Description text for the share</param>
    /// <param name="url">URL to the comic page</param>
    /// <returns>True if share was successful or user completed the action, false if cancelled or not supported</returns>
    /// <exception cref="ArgumentException">Thrown when title or url is null or empty</exception>
    public async Task<bool> ShareComicAsync(string title, string text, string url)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be null or empty", nameof(title));

        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        try
        {
            return await _jsRuntime.InvokeAsync<bool>("shareUtils.share", title, text ?? "", url);
        }
        catch (JSException)
        {
            // Web Share API not supported or user cancelled
            return false;
        }
    }

    /// <summary>
    /// Check if the Web Share API is supported in the current browser
    /// </summary>
    /// <returns>True if Web Share API is available</returns>
    public async Task<bool> IsShareSupportedAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("shareUtils.isSupported");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Copy URL to clipboard as a fallback for browsers without Web Share API
    /// </summary>
    /// <param name="url">URL to copy to clipboard</param>
    /// <exception cref="ArgumentException">Thrown when url is null or empty</exception>
    public async Task CopyToClipboardAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        await _jsRuntime.InvokeVoidAsync("shareUtils.copyToClipboard", url);
    }
}
