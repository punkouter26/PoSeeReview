using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PoSeeReview.Api.Features.Moderation;

/// <summary>
/// Registers the authorization policy guarding <c>/api/moderation</c>.
/// <para>
/// Kept in the slice rather than alongside <c>AddBffAuthentication</c>: the Auth slice would
/// otherwise have to know this slice's policy name and role setting, which is exactly the
/// cross-slice reference the layout forbids (NET_RULES 2.2). <c>AddAuthorizationBuilder</c> is
/// additive, so this composes with the deny-by-default fallback policy the Auth slice sets
/// rather than replacing it.
/// </para>
/// </summary>
public static class ModerationServiceCollectionExtensions
{
    public static IServiceCollection AddModerationAuthorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var requiredRole =
            configuration[$"{ModerationOptions.SectionName}:RequiredRole"] is { Length: > 0 } configured
                ? configured
                : new ModerationOptions().RequiredRole;

        services.AddAuthorizationBuilder()
            .AddPolicy(ModerationOptions.PolicyName, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(requiredRole));

        return services;
    }
}
