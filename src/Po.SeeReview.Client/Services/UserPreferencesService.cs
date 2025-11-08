using Microsoft.JSInterop;

namespace Po.SeeReview.Client.Services;

/// <summary>
/// Service for managing user preferences such as location permission state.
/// </summary>
public class UserPreferencesService
{
    private readonly LocalStorageService _localStorage;
    private const string LocationEnabledKey = "posee_location_enabled";

    public UserPreferencesService(LocalStorageService localStorage)
    {
        _localStorage = localStorage;
    }

    /// <summary>
    /// Saves that the user has granted location permission.
    /// </summary>
    public async Task SetLocationEnabledAsync(bool enabled)
    {
        await _localStorage.SetItemAsync(LocationEnabledKey, enabled.ToString().ToLower());
    }

    /// <summary>
    /// Checks if the user has previously granted location permission.
    /// </summary>
    public async Task<bool> IsLocationEnabledAsync()
    {
        var value = await _localStorage.GetItemAsync(LocationEnabledKey);
        return value == "true";
    }

    /// <summary>
    /// Clears the location permission preference.
    /// </summary>
    public async Task ClearLocationPreferenceAsync()
    {
        await _localStorage.RemoveItemAsync(LocationEnabledKey);
    }
}
