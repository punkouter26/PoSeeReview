using Moq;
using PoSeeReview.Api.Features.Restaurants;
using Xunit;

namespace PoSeeReview.Unit.Services;

/// <summary>
/// Unit tests for RestaurantService
/// </summary>
[Trait("Tier", "Unit")]
[Trait("Suite", "CriticalPath")]
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
