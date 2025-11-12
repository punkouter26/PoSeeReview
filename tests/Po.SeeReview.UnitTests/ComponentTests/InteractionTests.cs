using Bunit;
using Microsoft.AspNetCore.Components;
using Po.SeeReview.Client.Components;
using Po.SeeReview.Shared.Dtos;
using Xunit;

namespace Po.SeeReview.UnitTests.ComponentTests;

/// <summary>
/// Sample bUnit tests demonstrating user interaction patterns.
/// These tests verify component behavior in response to user actions like clicks, input changes, etc.
/// </summary>
public class InteractionTests : TestContext
{
    [Fact]
    public void RestaurantCard_WithEnabledCard_FiresOnClickCallback()
    {
        // Arrange
        var clickedRestaurant = (RestaurantDto?)null;
        var restaurant = new RestaurantDto
        {
            PlaceId = "test123",
            Name = "Test Restaurant",
            Address = "123 Test St",
            AverageRating = 4.5,
            TotalReviews = 10, // >= 5, so card is enabled
            Distance = 0.5
        };

        var cut = RenderComponent<RestaurantCard>(parameters => parameters
            .Add(p => p.Restaurant, restaurant)
            .Add(p => p.OnRestaurantClick, EventCallback.Factory.Create<RestaurantDto>(
                this, r => clickedRestaurant = r)));

        // Act - Click the card
        cut.Find(".restaurant-card").Click();

        // Assert - Callback should have been invoked with the restaurant
        Assert.NotNull(clickedRestaurant);
        Assert.Equal(restaurant.PlaceId, clickedRestaurant.PlaceId);
        Assert.Equal(restaurant.Name, clickedRestaurant.Name);
    }

    [Fact]
    public void RestaurantCard_WithDisabledCard_DoesNotFireCallback()
    {
        // Arrange
        var callbackInvoked = false;
        var restaurant = new RestaurantDto
        {
            PlaceId = "test456",
            Name = "Low Review Restaurant",
            Address = "456 Test Ave",
            AverageRating = 3.5,
            TotalReviews = 3, // < 5, so card is disabled
            Distance = 1.2
        };

        var cut = RenderComponent<RestaurantCard>(parameters => parameters
            .Add(p => p.Restaurant, restaurant)
            .Add(p => p.OnRestaurantClick, EventCallback.Factory.Create<RestaurantDto>(
                this, r => callbackInvoked = true)));

        // Act - Click the disabled card
        cut.Find(".restaurant-card").Click();

        // Assert - Callback should NOT have been invoked
        Assert.False(callbackInvoked);
    }

    [Fact]
    public void RestaurantCard_WithDisabledCard_ShowsDisabledOverlay()
    {
        // Arrange
        var restaurant = new RestaurantDto
        {
            PlaceId = "test789",
            Name = "New Restaurant",
            Address = "789 Test Blvd",
            AverageRating = 4.0,
            TotalReviews = 2, // < 5, so disabled
            Distance = 0.8
        };

        // Act
        var cut = RenderComponent<RestaurantCard>(parameters => parameters
            .Add(p => p.Restaurant, restaurant));

        // Assert - Disabled overlay should be present
        var overlay = cut.Find(".disabled-overlay");
        Assert.NotNull(overlay);
        
        var message = cut.Find(".disabled-message");
        Assert.Equal("Requires 5+ reviews", message.TextContent);
    }

    [Fact]
    public void RestaurantCard_WithEnabledCard_DoesNotShowDisabledOverlay()
    {
        // Arrange
        var restaurant = new RestaurantDto
        {
            PlaceId = "test101",
            Name = "Popular Restaurant",
            Address = "101 Test Dr",
            AverageRating = 4.8,
            TotalReviews = 50, // >= 5, so enabled
            Distance = 0.3
        };

        // Act
        var cut = RenderComponent<RestaurantCard>(parameters => parameters
            .Add(p => p.Restaurant, restaurant));

        // Assert - Disabled overlay should NOT be present
        var overlays = cut.FindAll(".disabled-overlay");
        Assert.Empty(overlays);
    }

    [Fact]
    public void RestaurantCard_AppliesDisabledClass_WhenReviewsAreLow()
    {
        // Arrange
        var restaurant = new RestaurantDto
        {
            PlaceId = "test202",
            Name = "Test",
            Address = "Test",
            AverageRating = 4.0,
            TotalReviews = 4, // < 5
            Distance = 1.0
        };

        // Act
        var cut = RenderComponent<RestaurantCard>(parameters => parameters
            .Add(p => p.Restaurant, restaurant));

        // Assert - Card should have "disabled" class
        var card = cut.Find(".restaurant-card");
        Assert.Contains("disabled", card.ClassList);
    }
}
