using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Shared.Dtos;

/// <summary>
/// Response from GET /api/restaurants/nearby and GET /api/restaurants/search
/// </summary>
public class NearbyRestaurantsResponse
{
    public List<RestaurantDto> Restaurants { get; set; } = new();
    public int TotalCount { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}
