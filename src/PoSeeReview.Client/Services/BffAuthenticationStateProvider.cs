using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Client.Services;

/// <summary>
/// Custom AuthenticationStateProvider (NET_RULES 4.1): auth state is whatever the server
/// says at <c>/auth/me</c> — the BFF cookie is the session, the client holds no tokens.
/// </summary>
public sealed class BffAuthenticationStateProvider(HttpClient http) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    // /auth/me is hit on every outgoing API request; cache the resolved state so we make at most
    // one round-trip per auth change. Invalidated by NotifyStateChanged() after login/logout.
    private Task<AuthenticationState>? _cachedState;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
        => _cachedState ??= FetchAuthenticationStateAsync();

    private async Task<AuthenticationState> FetchAuthenticationStateAsync()
    {
        AuthStateDto? state;
        // This task is cached for the session and gates every AuthorizeView, so it must always
        // complete: a stalled request would hold the page on "Authorizing..." for HttpClient's
        // 100s default, and a timeout or a non-JSON body (a proxy error page) would fault it for good.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            state = await http.GetFromJsonAsync("auth/me", AppJsonContext.Default.AuthStateDto, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or NotSupportedException)
        {
            return Anonymous;
        }

        if (state?.IsAuthenticated != true || string.IsNullOrEmpty(state.UserId))
        {
            return Anonymous;
        }

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, state.UserId),
            new(ClaimTypes.Name, state.Name ?? state.Email ?? state.UserId),
            .. state.Roles.Select(role => new Claim(ClaimTypes.Role, role))
        ];
        if (!string.IsNullOrEmpty(state.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, state.Email));
        }

        return new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity(claims, state.AuthScheme ?? "BFF")));
    }

    /// <summary>Re-queries /auth/me after a login/logout navigation completes.</summary>
    public void NotifyStateChanged()
    {
        _cachedState = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
