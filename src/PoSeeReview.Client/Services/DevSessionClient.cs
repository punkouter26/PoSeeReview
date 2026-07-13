using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Client.Services;

public class DevSessionClient(
    HttpClient httpClient,
    IJSRuntime jsRuntime,
    DevSessionStateService sessionState,
    AuthenticationStateProvider authenticationStateProvider,
    IWebAssemblyHostEnvironment hostEnvironment,
    ILogger<DevSessionClient> logger)
{
    private const string DevUserHeader = "X-Dev-User-Id";
    private const string SessionHeader = "X-Session-ID";
    private const string CorrelationHeader = "X-Correlation-ID";
    private const string StorageKey = "posee_dev_session";
    private const string SessionIdStorageKey = "posee_session_id";
    private DevSessionDto? _cachedSession;
    private string? _sessionId;

    // The server trusts X-Dev-User-Id for identity, so it must only ever be sent from
    // non-production environments where the dev-bypass flow is legitimate.
    private bool IsDevOrTest =>
        hostEnvironment.IsEnvironment("Development") || hostEnvironment.IsEnvironment("Test");

    public async Task<DevSessionDto?> GetCurrentSessionAsync(bool refreshFromServer = false, CancellationToken cancellationToken = default)
    {
        var authenticatedSession = await BuildAuthenticatedSessionAsync();
        if (authenticatedSession != null)
        {
            if (!SessionsEqual(_cachedSession, authenticatedSession))
            {
                logger.LogInformation("Refreshing dev session from authenticated Microsoft account for user {UserId}", authenticatedSession.UserId);
                _cachedSession = authenticatedSession;
                await PersistSessionAsync(authenticatedSession);
                sessionState.Set(authenticatedSession);
            }

            return authenticatedSession;
        }

        if (!refreshFromServer && _cachedSession != null)
        {
            return _cachedSession;
        }

        if (!refreshFromServer)
        {
            var storedSession = await ReadStoredSessionAsync();
            if (storedSession != null)
            {
                _cachedSession = storedSession;
                return storedSession;
            }
        }

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/devsession");
        var existingSession = _cachedSession ?? await ReadStoredSessionAsync();
        if (IsDevOrTest && !string.IsNullOrWhiteSpace(existingSession?.UserId))
        {
            request.Headers.Add(DevUserHeader, existingSession.UserId);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return _cachedSession;
        }

        _cachedSession = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.DevSessionDto, cancellationToken);
        if (_cachedSession != null)
        {
            await PersistSessionAsync(_cachedSession);
            sessionState.Set(_cachedSession);
        }

        return _cachedSession;
    }

    public async Task<DevSessionDto> CreateRandomAnonymousSessionAsync(CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/devsession/anon");
        await AttachStoredHeaderAsync(request);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        _cachedSession = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.DevSessionDto, cancellationToken)
            ?? throw new InvalidOperationException("Dev session response was null.");

        await PersistSessionAsync(_cachedSession);
        sessionState.Set(_cachedSession);
        return _cachedSession;
    }

    public async Task<string?> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        var session = await GetCurrentSessionAsync(cancellationToken: cancellationToken);
        return session?.UserId;
    }

    public async Task LogoutAsync()
    {
        _cachedSession = null;
        sessionState.Clear();
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
    }

    public async Task AttachStoredHeaderAsync(HttpRequestMessage request)
    {
        var session = await GetCurrentSessionAsync();
        if (IsDevOrTest && !string.IsNullOrWhiteSpace(session?.UserId))
        {
            request.Headers.Remove(DevUserHeader);
            request.Headers.Add(DevUserHeader, session.UserId);
        }

        // Stable per-browser session id so server-side telemetry can correlate a single user's
        // journey across requests (the API enriches logs/traces from X-Session-ID).
        var sessionId = await GetOrCreateSessionIdAsync();
        request.Headers.Remove(SessionHeader);
        request.Headers.Add(SessionHeader, sessionId);

        // Fresh per-request correlation id (§6.9) so the server ties its logs/traces back to this
        // exact request instead of generating its own when the header is absent.
        request.Headers.Remove(CorrelationHeader);
        request.Headers.Add(CorrelationHeader, Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Returns a stable session id, generating and persisting one on first use.
    /// Survives navigation/reload via localStorage; unique per browser profile.
    /// </summary>
    private async Task<string> GetOrCreateSessionIdAsync()
    {
        if (!string.IsNullOrWhiteSpace(_sessionId))
        {
            return _sessionId;
        }

        _sessionId = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", SessionIdStorageKey);
        if (string.IsNullOrWhiteSpace(_sessionId))
        {
            _sessionId = Guid.NewGuid().ToString("N");
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", SessionIdStorageKey, _sessionId);
        }

        return _sessionId;
    }

    private async Task<DevSessionDto?> BuildAuthenticatedSessionAsync()
    {
        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var email = user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("preferred_username")?.Value
            ?? user.FindFirst("emails")?.Value;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("oid")?.Value
            ?? user.Identity?.Name
            ?? email;

        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning("Authenticated Microsoft identity is missing a usable user identifier");
            return null;
        }

        // Guest cookie sessions (auth/login/fake) surface as anonymous dev-bypass identities.
        var isGuest = user.IsInRole("Guest");
        return new DevSessionDto
        {
            UserId = userId,
            Email = email,
            IsAnonymous = isGuest,
            IsDevelopmentBypass = isGuest,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static bool SessionsEqual(DevSessionDto? left, DevSessionDto? right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        return string.Equals(left.UserId, right.UserId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Email, right.Email, StringComparison.OrdinalIgnoreCase)
            && left.IsAnonymous == right.IsAnonymous;
    }

    private async Task<DevSessionDto?> ReadStoredSessionAsync()
    {
        var raw = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(raw, AppJsonContext.Default.DevSessionDto);
        }
        catch (JsonException)
        {
            await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
            return null;
        }
    }

    private async Task PersistSessionAsync(DevSessionDto session)
    {
        var raw = JsonSerializer.Serialize(session, AppJsonContext.Default.DevSessionDto);
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, raw);
    }
}
