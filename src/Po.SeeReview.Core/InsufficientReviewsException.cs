namespace Po.SeeReview.Core;

/// <summary>
/// Thrown when a restaurant does not have enough reviews (or appropriate reviews after filtering)
/// to generate a comic strip. Extends InvalidOperationException so callers that handle the
/// general case continue to work while specific callers can catch this precise type.
/// </summary>
public sealed class InsufficientReviewsException : InvalidOperationException
{
    public InsufficientReviewsException(string message) : base(message) { }

    public InsufficientReviewsException(string message, Exception innerException)
        : base(message, innerException) { }
}
