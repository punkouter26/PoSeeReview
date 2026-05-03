using System.Security.Cryptography;
using Po.SeeReview.Application.Abstractions;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Application.DevSessions;

public class DevSessionCommandHandler(ICurrentRequestIdentityAccessor currentRequestIdentityAccessor)
{
    public DevSessionDto GetCurrentSession()
    {
        var currentUserId = currentRequestIdentityAccessor.GetCurrentUserId();
        var email = currentRequestIdentityAccessor.GetCurrentUserEmail();
        return BuildSession(currentUserId, email);
    }

    public DevSessionDto CreateRandomAnonymousSession()
    {
        var suffix = RandomNumberGenerator.GetInt32(100000, 999999);
        return BuildSession($"ANON{suffix}", email: null);
    }

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
