namespace Po.SeeReview.Core.Utilities;

/// <summary>
/// Geospatial utility methods for distance calculations.
/// Uses Haversine formula for great-circle distance between two points.
/// </summary>
public static class GeoUtils
{
    private const double EarthRadiusKm = 6371;

    /// <summary>
    /// Calculates the great-circle distance between two points on Earth.
    /// </summary>
    /// <param name="lat1">Latitude of first point in degrees</param>
    /// <param name="lon1">Longitude of first point in degrees</param>
    /// <param name="lat2">Latitude of second point in degrees</param>
    /// <param name="lon2">Longitude of second point in degrees</param>
    /// <returns>Distance in kilometers</returns>
    public static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusKm * c;
    }

    /// <summary>
    /// Converts degrees to radians.
    /// </summary>
    public static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }

    /// <summary>
    /// Converts radians to degrees.
    /// </summary>
    public static double RadiansToDegrees(double radians)
    {
        return radians * 180 / Math.PI;
    }
}
