using Microsoft.JSInterop;

namespace PoSeeReview.Client.Services;

/// <summary>
/// Provides Web Audio API integration for app-appropriate sounds.
/// Uses the browser's native AudioContext for UI feedback sounds.
/// </summary>
public sealed class AudioService(IJSRuntime jsRuntime)
{
    private readonly IJSRuntime _jsRuntime = jsRuntime;

    /// <summary>
    /// Plays a short success/completion sound.
    /// </summary>
    public async Task PlaySuccessAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("playSuccessSound");
        }
        catch (JSException)
        {
            // Silently ignore if audio is not available
        }
    }

    /// <summary>
    /// Plays a short error/notification sound.
    /// </summary>
    public async Task PlayErrorAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("playErrorSound");
        }
        catch (JSException)
        {
            // Silently ignore if audio is not available
        }
    }

    /// <summary>
    /// Plays a subtle click/tap feedback sound.
    /// </summary>
    public async Task PlayClickAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("playClickSound");
        }
        catch (JSException)
        {
            // Silently ignore if audio is not available
        }
    }
}
