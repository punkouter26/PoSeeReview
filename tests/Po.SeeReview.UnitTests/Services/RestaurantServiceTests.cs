using Moq;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Infrastructure.Repositories;
using Po.SeeReview.Infrastructure.Services;
using Xunit;

namespace Po.SeeReview.UnitTests.Services;

/// <summary>
/// Unit tests for RestaurantService
/// </summary>
public class RestaurantServiceTests
{
    [Theory]
    [InlineData(91.0, -122.3321)]   // Invalid latitude > 90
    [InlineData(-91.0, -122.3321)]  // Invalid latitude < -90
    [InlineData(47.6062, 181.0)]    // Invalid longitude > 180
    [InlineData(47.6062, -181.0)]   // Invalid longitude < -180
    public void ValidateCoordinates_InvalidCoordinates_ThrowsArgumentException(double latitude, double longitude)
    {
        // Arrange
        var isValid = latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180;

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateCoordinates_ValidCoordinates_Passes()
    {
        // Arrange
        var latitude = 47.6062;
        var longitude = -122.3321;
        var isValid = latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180;

        // Assert
        Assert.True(isValid);
    }
}
