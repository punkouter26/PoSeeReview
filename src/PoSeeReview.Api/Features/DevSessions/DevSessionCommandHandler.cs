using System.Security.Cryptography;
using PoSeeReview.Api.Identity;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.DevSessions;

public class DevSessionCommandHandler(ICurrentRequestIdentityAccessor currentRequestIdentityAccessor)
{
    public DevSessionDto GetCurrentSession()
    {
        var currentUserId = currentRequestIdentityAccessor.GetCurrentUserId();
        var email = currentRequestIdentityAccessor.GetCurrentUserEmail();
        return BuildSession(currentUserId.Value, email);
    }

    public DevSessionDto CreateRandomAnonymousSession()
    {
        var suffix = RandomNumberGenerator.GetInt32(100000, 999999);
        return BuildSession($"ANON{suffix}", email: null);
    }

    /// <summary>
    /// Public, dependency-free factory used by the controller's Production fallback path
    /// so we never return 404 from /api/devsession in deployed environments.
    /// </summary>
    public static DevSessionDto BuildAnonymousSession() =>
        BuildSession(userId: null, email: null);

    private static DevSessionDto BuildSession(string? userId, string? email, TimeProvider? timeProvider = null)
    {
        var normalizedUserId = string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId;
        var isAnon = normalizedUserId.StartsWith("ANON", StringComparison.OrdinalIgnoreCase);
        var tp = timeProvider ?? TimeProvider.System;

        return new DevSessionDto
        {
            UserId = normalizedUserId,
            Email = isAnon ? null : email,
            IsAnonymous = isAnon,
            IsDevelopmentBypass = isAnon,
            CreatedAtUtc = tp.GetUtcNow().UtcDateTime
        };
    }
}
