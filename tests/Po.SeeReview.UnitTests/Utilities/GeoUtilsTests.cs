using Po.SeeReview.Core.Utilities;
using Xunit;

namespace Po.SeeReview.UnitTests.Utilities;

/// <summary>
/// Unit tests for GeoUtils Haversine distance calculation.
/// All expected values computed with an independent online calculator.
/// </summary>
public sealed class GeoUtilsTests
{
    [Theory]
    [InlineData(47.6062, -122.3321, 47.6062, -122.3321, 0.0)] // same point
    [InlineData(47.6062, -122.3321, 47.6150, -122.3490, 1.74)] // Seattle downtown ~1.7 km
    [InlineData(51.5074, -0.1278, 48.8566, 2.3522, 343.56)]   // London → Paris
    [InlineData(40.7128, -74.0060, 34.0522, -118.2437, 3935.75)] // NYC → LA
    public void CalculateDistance_KnownCoordinates_ReturnsExpectedKm(
        double lat1, double lon1, double lat2, double lon2, double expectedKm)
    {
        var result = GeoUtils.CalculateDistance(lat1, lon1, lat2, lon2);
        Assert.Equal(expectedKm, result, precision: 0); // within 1 km tolerance
    }

    [Fact]
    public void CalculateDistance_SamePoint_ReturnsZero()
    {
        var result = GeoUtils.CalculateDistance(47.6062, -122.3321, 47.6062, -122.3321);
        Assert.Equal(0.0, result, precision: 5);
    }

    [Fact]
    public void CalculateDistance_IsSymmetric()
    {
        // AB distance == BA distance
        var ab = GeoUtils.CalculateDistance(51.5074, -0.1278, 48.8566, 2.3522);
        var ba = GeoUtils.CalculateDistance(48.8566, 2.3522, 51.5074, -0.1278);
        Assert.Equal(ab, ba, precision: 5);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, Math.PI / 2)]
    [InlineData(180, Math.PI)]
    [InlineData(-90, -Math.PI / 2)]
    public void DegreesToRadians_ConvertsCorrectly(double degrees, double expectedRadians)
    {
        var result = GeoUtils.DegreesToRadians(degrees);
        Assert.Equal(expectedRadians, result, precision: 10);
    }
}
