using Azure;
using Po.SeeReview.Core.Utilities;
using Xunit;

namespace Po.SeeReview.UnitTests.Utilities;

/// <summary>
/// Unit tests for AzureRetryUtils transient failure detection.
/// </summary>
public sealed class AzureRetryUtilsTests
{
    [Theory]
    [InlineData(408)] // Request Timeout
    [InlineData(429)] // Too Many Requests
    [InlineData(500)] // Internal Server Error
    [InlineData(502)] // Bad Gateway
    [InlineData(503)] // Service Unavailable
    [InlineData(504)] // Gateway Timeout
    public void IsTransientFailure_TransientStatusCodes_ReturnsTrue(int statusCode)
    {
        var exception = new RequestFailedException(statusCode, "transient error");
        Assert.True(AzureRetryUtils.IsTransientFailure(exception));
    }

    [Theory]
    [InlineData(400)] // Bad Request
    [InlineData(401)] // Unauthorized
    [InlineData(403)] // Forbidden
    [InlineData(404)] // Not Found
    [InlineData(409)] // Conflict
    [InlineData(412)] // Precondition Failed
    public void IsTransientFailure_NonTransientStatusCodes_ReturnsFalse(int statusCode)
    {
        var exception = new RequestFailedException(statusCode, "non-transient error");
        Assert.False(AzureRetryUtils.IsTransientFailure(exception));
    }
}
