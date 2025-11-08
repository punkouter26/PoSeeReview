using Microsoft.JSInterop;

namespace Po.SeeReview.Client.Services;

/// <summary>
/// Service for browser geolocation functionality using JavaScript interop.
/// </summary>
public class GeolocationService
{
    private readonly IJSRuntime _jsRuntime;

    public GeolocationService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Gets the user's current position using the browser's geolocation API.
    /// </summary>
    /// <returns>GeolocationResult containing latitude and longitude, or error information.</returns>
    public async Task<GeolocationResult> GetCurrentPositionAsync()
    {
        try
        {
            var result = await _jsRuntime.InvokeAsync<GeolocationResult>("geolocation.getCurrentPosition");
            return result;
        }
        catch (JSException ex)
        {
            return new GeolocationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Checks if geolocation is supported in the browser.
    /// </summary>
    public async Task<bool> IsGeolocationSupportedAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("geolocation.isSupported");
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Result from geolocation API call.
/// </summary>
public class GeolocationResult
{
    public bool Success { get; set; } = true;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Accuracy { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
