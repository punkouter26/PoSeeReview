using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Identity;

/// <summary>
/// Abstraction for reading the current request identity without coupling handlers to ASP.NET.
/// </summary>
public interface ICurrentRequestIdentityAccessor
{
    UserId GetCurrentUserId();
    string? GetCurrentUserEmail();
}
