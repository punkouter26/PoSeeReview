using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Client.Services;

public class DevSessionClient(
    HttpClient httpClient,
    IJSRuntime jsRuntime,
    DevSessionStateService sessionState,
    AuthenticationStateProvider authenticationStateProvider,
    ILogger<DevSessionClient> logger)
{
    private const string DevUserHeader = "X-Dev-User-Id";
    private const string StorageKey = "posee_dev_session";
    private DevSessionDto? _cachedSession;

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
        if (!string.IsNullOrWhiteSpace(existingSession?.UserId))
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

    public async Task PrepareForMicrosoftLoginAsync(CancellationToken cancellationToken = default)
    {
        var session = await GetCurrentSessionAsync(cancellationToken: cancellationToken);
        if (session?.IsAnonymous == true)
        {
            logger.LogInformation("Clearing stale anonymous dev session {UserId} before Microsoft login", session.UserId);
            await LogoutAsync();
        }
    }

    public async Task<DevSessionDto?> SyncFromMicrosoftIdentityAsync(CancellationToken cancellationToken = default)
    {
        var authenticatedSession = await BuildAuthenticatedSessionAsync();
        if (authenticatedSession == null)
        {
            logger.LogWarning("Microsoft identity sync requested but no authenticated identity is present");
            return null;
        }

        _cachedSession = authenticatedSession;
        await PersistSessionAsync(authenticatedSession);
        sessionState.Set(authenticatedSession);
        logger.LogInformation("Microsoft identity synchronized to dev session for user {UserId}", authenticatedSession.UserId);
        return authenticatedSession;
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
        if (!string.IsNullOrWhiteSpace(session?.UserId))
        {
            request.Headers.Remove(DevUserHeader);
            request.Headers.Add(DevUserHeader, session.UserId);
        }
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

        return new DevSessionDto
        {
            UserId = userId,
            Email = email,
            IsAnonymous = false,
            IsDevelopmentBypass = false,
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
