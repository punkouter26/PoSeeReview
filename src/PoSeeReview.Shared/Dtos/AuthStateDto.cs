namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// Server-side authentication state returned by <c>/auth/me</c> (NET_RULES 4.4).
/// The BFF cookie is the only session artifact; the WASM client renders from this DTO.
/// </summary>
public sealed class AuthStateDto
{
    public bool IsAuthenticated { get; init; }
    public string? UserId { get; init; }
    public string? Name { get; init; }
    public string? Email { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public string? AuthScheme { get; init; }

    /// <summary>
    /// Echo of the requested returnUrl after open-redirect sanitization (local paths only).
    /// </summary>
    public string ReturnUrl { get; init; } = "/";
}
