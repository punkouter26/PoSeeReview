using Microsoft.JSInterop;

namespace PoSeeReview.Client.Services;

/// <summary>
/// How a Web Share attempt ended. Cancelling is deliberately distinct from "unsupported":
/// a user who dismisses the share sheet wants nothing to happen, whereas a browser without
/// the API needs the clipboard fallback.
/// </summary>
public enum ShareOutcome
{
    Shared,
    Cancelled,
    Unsupported
}

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
    /// Share a comic using the Web Share API.
    /// </summary>
    /// <param name="title">Title of the comic (restaurant name)</param>
    /// <param name="text">Description text for the share</param>
    /// <param name="url">URL to the comic page</param>
    /// <returns>Whether the share completed, was cancelled by the user, or is unsupported.</returns>
    /// <exception cref="ArgumentException">Thrown when title or url is null or empty</exception>
    public async Task<ShareOutcome> ShareComicAsync(string title, string text, string url)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be null or empty", nameof(title));

        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        try
        {
            var outcome = await _jsRuntime.InvokeAsync<string>("shareUtils.share", title, text ?? "", url);
            return outcome switch
            {
                "shared" => ShareOutcome.Shared,
                "cancelled" => ShareOutcome.Cancelled,
                _ => ShareOutcome.Unsupported
            };
        }
        catch (JSException)
        {
            return ShareOutcome.Unsupported;
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
