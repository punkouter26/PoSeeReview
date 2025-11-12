using Azure;

namespace Po.SeeReview.Core.Utilities;

/// <summary>
/// Utility methods for Azure service retry logic
/// </summary>
public static class AzureRetryUtils
{
    /// <summary>
    /// Determines if an Azure RequestFailedException represents a transient failure
    /// that should be retried (timeouts, rate limits, server errors)
    /// </summary>
    public static bool IsTransientFailure(RequestFailedException exception)
    {
        return exception.Status is 408 or 429 or >= 500;
    }
}
