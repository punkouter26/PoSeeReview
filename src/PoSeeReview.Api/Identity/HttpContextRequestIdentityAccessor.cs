using System.Security.Claims;
using Microsoft.Extensions.Hosting;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Identity;

public class HttpContextRequestIdentityAccessor(
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment environment) : ICurrentRequestIdentityAccessor
{
    private const string CanonicalHeader = "X-Dev-User-Id";
    private const string LegacyHeader = "X-Dev-UserId";

    public UserId GetCurrentUserId()
    {
        var context = httpContextAccessor.HttpContext;
        if (context == null)
        {
            return UserId.Anonymous;
        }

        // Header-based identity override is a Dev/Test affordance for local tooling and E2E.
        // NEVER trust a client-supplied header for identity in Production — the authenticated
        // cookie principal is the only source of truth there.
        if (!environment.IsProduction())
        {
            if (context.Request.Headers.TryGetValue(CanonicalHeader, out var canonicalDevUserId)
                && !string.IsNullOrWhiteSpace(canonicalDevUserId))
            {
                return UserId.From(canonicalDevUserId.ToString());
            }

            if (context.Request.Headers.TryGetValue(LegacyHeader, out var legacyDevUserId)
                && !string.IsNullOrWhiteSpace(legacyDevUserId))
            {
                return UserId.From(legacyDevUserId.ToString());
            }
        }

        return UserId.From(
            context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User?.Identity?.Name);
    }

    public string? GetCurrentUserEmail()
    {
        var context = httpContextAccessor.HttpContext;
        if (context == null)
        {
            return null;
        }

        return context.User?.FindFirstValue(ClaimTypes.Email)
            ?? context.User?.FindFirstValue("email");
    }
}
