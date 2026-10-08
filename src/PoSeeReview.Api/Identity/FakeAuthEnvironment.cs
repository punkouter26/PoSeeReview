using PoSeeReview.Shared;

namespace PoSeeReview.Api.Identity;

internal static class FakeAuthEnvironment
{
    /// <summary>
    /// An allow-list, not "anything but Production": an environment named Staging must not
    /// accept <c>X-Fake-Roles: Moderator</c> just because nobody thought to deny it.
    /// </summary>
    public static bool AllowsFakeAuth(this IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment(HostEnvironments.Test);
}
