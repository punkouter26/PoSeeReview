namespace Po.SeeReview.Application.Abstractions;

/// <summary>
/// Abstraction for reading the current request identity without coupling handlers to ASP.NET.
/// </summary>
public interface ICurrentRequestIdentityAccessor
{
    string GetCurrentUserId();
    string? GetCurrentUserEmail();
}
