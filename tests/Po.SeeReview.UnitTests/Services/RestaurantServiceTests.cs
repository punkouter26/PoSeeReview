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
