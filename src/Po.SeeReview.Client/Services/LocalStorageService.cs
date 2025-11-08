using Microsoft.JSInterop;

namespace Po.SeeReview.Client.Services;

/// <summary>
/// Service for interacting with browser localStorage to persist user preferences.
/// </summary>
public class LocalStorageService
{
    private readonly IJSRuntime _jsRuntime;

    public LocalStorageService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Saves a value to localStorage.
    /// </summary>
    public async Task SetItemAsync(string key, string value)
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", key, value);
    }

    /// <summary>
    /// Retrieves a value from localStorage.
    /// </summary>
    public async Task<string?> GetItemAsync(string key)
    {
        return await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);
    }

    /// <summary>
    /// Removes a value from localStorage.
    /// </summary>
    public async Task RemoveItemAsync(string key)
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
    }

    /// <summary>
    /// Clears all values from localStorage.
    /// </summary>
    public async Task ClearAsync()
    {
        await _jsRuntime.InvokeVoidAsync("localStorage.clear");
    }
}
