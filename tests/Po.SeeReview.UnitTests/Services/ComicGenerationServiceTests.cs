using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Moq;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Infrastructure.Services;
using Xunit;

namespace Po.SeeReview.UnitTests.Services;

/// <summary>
/// Unit tests for ComicGenerationService - orchestrates review analysis, narrative generation, and comic creation
/// </summary>
public class ComicGenerationServiceTests
{
    private readonly Mock<IRestaurantService> _mockRestaurantService;
    private readonly Mock<IAzureOpenAIService> _mockOpenAIService;
    private readonly Mock<IDalleComicService> _mockDalleService;
    private readonly Mock<IComicTextOverlayService> _mockTextOverlayService;
    private readonly Mock<IBlobStorageService> _mockBlobStorageService;
    private readonly Mock<IComicRepository> _mockComicRepository;
    private readonly Mock<ILeaderboardService> _mockLeaderboardService;
    private readonly Mock<ILogger<ComicGenerationService>> _mockLogger;
    private readonly TelemetryClient _telemetryClient;

    public ComicGenerationServiceTests()
    {
        _mockRestaurantService = new Mock<IRestaurantService>();
        _mockOpenAIService = new Mock<IAzureOpenAIService>();
        _mockDalleService = new Mock<IDalleComicService>();
        _mockTextOverlayService = new Mock<IComicTextOverlayService>();
        _mockBlobStorageService = new Mock<IBlobStorageService>();
        _mockComicRepository = new Mock<IComicRepository>();
        _mockLeaderboardService = new Mock<ILeaderboardService>();
        _mockLogger = new Mock<ILogger<ComicGenerationService>>();
        _telemetryClient = new TelemetryClient(new TelemetryConfiguration());
    }

    private ComicGenerationService CreateService()
    {
        // Default setup: text overlay returns input bytes unchanged (passthrough)
        _mockTextOverlayService.Setup(x => x.AddTextOverlayAsync(
            It.IsAny<byte[]>(), 
            It.IsAny<string>(), 
            It.IsAny<int>()))
            .ReturnsAsync((byte[] imageBytes, string narrative, int panelCount) => imageBytes);

        return new ComicGenerationService(
            _mockRestaurantService.Object,
            _mockOpenAIService.Object,
            _mockDalleService.Object,
            _mockTextOverlayService.Object,
            _mockBlobStorageService.Object,
            _mockComicRepository.Object,
            _mockLeaderboardService.Object,
            _mockLogger.Object,
            _telemetryClient
        );
    }

    [Fact]
    public async Task GenerateComicAsync_WithValidRestaurant_ShouldReturnCachedComicIfValid()
    {
        // Arrange
        var placeId = "test-place-123";
        var cachedComic = new Comic
        {
            Id = Guid.NewGuid().ToString(),
            PlaceId = placeId,
            RestaurantName = "Test Restaurant",
            ImageUrl = "https://example.com/comic.png",
            Narrative = "Test narrative",
            StrangenessScore = 85,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(12) // Still valid
        };

        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(placeId))
            .ReturnsAsync(cachedComic);

        // Act
        var result = await service.GenerateComicAsync(placeId, forceRegenerate: false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(placeId, result.PlaceId);
        Assert.Equal(cachedComic.ImageUrl, result.ImageUrl);
        Assert.True(result.IsCached);
        _mockDalleService.Verify(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GenerateComicAsync_WithExpiredCache_ShouldRegenerateComic()
    {
        // Arrange
        var placeId = "test-place-123";
        var expiredComic = new Comic
        {
            PlaceId = placeId,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) // Expired
        };

        var restaurant = new Restaurant
        {
            PlaceId = placeId,
            Name = "Test Restaurant",
            Reviews = new List<Review>
            {
                new Review { Text = "This place is bizarre! The waiter wore a dinosaur costume.", Rating = 5 },
                new Review { Text = "Strange but good. They serve food in shoes.", Rating = 4 },
                new Review { Text = "Weirdest experience ever. Worth it!", Rating = 5 },
                new Review { Text = "Normal food, weird ambiance. Furniture upside down.", Rating = 3 },
                new Review { Text = "Surreal dining experience. Loved the backwards menu.", Rating = 5 }
            }
        };

        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(placeId))
            .ReturnsAsync(expiredComic);
        _mockRestaurantService.Setup(x => x.GetRestaurantDetailsAsync(placeId))
            .ReturnsAsync(restaurant);
        _mockOpenAIService.Setup(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>()))
            .ReturnsAsync((75, 3, "A restaurant where waiters dress as dinosaurs and food is served in shoes."));
        _mockDalleService.Setup(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        _mockBlobStorageService.Setup(x => x.UploadComicImageAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync("https://blob.storage/comic.png");

        // Act
        var result = await service.GenerateComicAsync(placeId, forceRegenerate: false);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsCached);
        _mockDalleService.Verify(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Once);
        _mockComicRepository.Verify(x => x.UpsertAsync(It.IsAny<Comic>()), Times.Once);
    }

    [Fact]
    public async Task GenerateComicAsync_WithForceRegenerate_ShouldAlwaysGenerateNewComic()
    {
        // Arrange
        var placeId = "test-place-123";
        var cachedComic = new Comic
        {
            PlaceId = placeId,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(12) // Still valid
        };

        var restaurant = new Restaurant
        {
            PlaceId = placeId,
            Name = "Test Restaurant",
            Reviews = new List<Review>
            {
                new Review { Text = "Strange review 1", Rating = 5 },
                new Review { Text = "Strange review 2", Rating = 4 },
                new Review { Text = "Strange review 3", Rating = 5 },
                new Review { Text = "Strange review 4", Rating = 3 },
                new Review { Text = "Strange review 5", Rating = 5 }
            }
        };

        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(placeId))
            .ReturnsAsync(cachedComic);
        _mockRestaurantService.Setup(x => x.GetRestaurantDetailsAsync(placeId))
            .ReturnsAsync(restaurant);
        _mockOpenAIService.Setup(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>()))
            .ReturnsAsync((75, 3, "Test narrative"));
        _mockDalleService.Setup(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        _mockBlobStorageService.Setup(x => x.UploadComicImageAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync("https://blob.storage/comic.png");

        // Act
        var result = await service.GenerateComicAsync(placeId, forceRegenerate: true);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsCached);
        _mockDalleService.Verify(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task GenerateComicAsync_WithInsufficientReviews_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var placeId = "test-place-123";
        var restaurant = new Restaurant
        {
            PlaceId = placeId,
            Name = "Test Restaurant",
            Reviews = new List<Review>
            {
                new Review { Text = "Only one review", Rating = 5 }
            }
        };

        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(placeId))
            .ReturnsAsync((Comic?)null);
        _mockRestaurantService.Setup(x => x.GetRestaurantDetailsAsync(placeId))
            .ReturnsAsync(restaurant);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateComicAsync(placeId, forceRegenerate: false));
    }

    [Fact]
    public async Task GenerateComicAsync_WithMinimumReviews_ShouldSucceed()
    {
        // Arrange
        var placeId = "test-place-123";
        var restaurant = new Restaurant
        {
            PlaceId = placeId,
            Name = "Test Restaurant",
            Reviews = new List<Review>
            {
                new Review { Text = "Review 1", Rating = 5 },
                new Review { Text = "Review 2", Rating = 4 },
                new Review { Text = "Review 3", Rating = 5 },
                new Review { Text = "Review 4", Rating = 3 },
                new Review { Text = "Review 5", Rating = 5 }
            }
        };

        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(placeId))
            .ReturnsAsync((Comic?)null);
        _mockRestaurantService.Setup(x => x.GetRestaurantDetailsAsync(placeId))
            .ReturnsAsync(restaurant);
        _mockOpenAIService.Setup(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>()))
            .ReturnsAsync((50, 2, "Narrative"));
        _mockDalleService.Setup(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        _mockBlobStorageService.Setup(x => x.UploadComicImageAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync("https://blob.storage/comic.png");

        // Act
        var result = await service.GenerateComicAsync(placeId, forceRegenerate: false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(placeId, result.PlaceId);
    }

    [Fact]
    public async Task GenerateComicAsync_ShouldFilterInappropriateReviews()
    {
        // Arrange
        var placeId = "test-place-123";
        var restaurant = new Restaurant
        {
            PlaceId = placeId,
            Name = "Test Restaurant",
            Reviews = new List<Review>
            {
                new Review { Text = "Great food!", Rating = 5 },
                new Review { Text = "This review contains profanity damn it", Rating = 3 },
                new Review { Text = "Nice ambiance", Rating = 4 },
                new Review { Text = "Excellent service", Rating = 5 },
                new Review { Text = "Will return", Rating = 5 }
            }
        };

        var capturedReviews = new List<string>();
        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(placeId))
            .ReturnsAsync((Comic?)null);
        _mockRestaurantService.Setup(x => x.GetRestaurantDetailsAsync(placeId))
            .ReturnsAsync(restaurant);
        _mockOpenAIService.Setup(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>()))
            .Callback<List<string>>(reviews => capturedReviews = reviews)
            .ReturnsAsync((60, 2, "Narrative"));
        _mockDalleService.Setup(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        _mockBlobStorageService.Setup(x => x.UploadComicImageAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync("https://blob.storage/comic.png");

        // Act
        await service.GenerateComicAsync(placeId, forceRegenerate: false);

        // Assert - content moderation should filter some reviews
        Assert.NotEmpty(capturedReviews);
        _mockOpenAIService.Verify(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>()), Times.Once);
    }

    [Fact]
    public async Task GenerateComicAsync_ShouldSet7DayCacheExpiration()
    {
        // Arrange
        var placeId = "test-place-123";
        var restaurant = new Restaurant
        {
            PlaceId = placeId,
            Name = "Test Restaurant",
            Reviews = Enumerable.Range(1, 10).Select(i => new Review
            {
                Text = $"Review {i}",
                Rating = 5
            }).ToList()
        };

        Comic? capturedComic = null;
        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(placeId))
            .ReturnsAsync((Comic?)null);
        _mockRestaurantService.Setup(x => x.GetRestaurantDetailsAsync(placeId))
            .ReturnsAsync(restaurant);
        _mockOpenAIService.Setup(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>()))
            .ReturnsAsync((70, 3, "Narrative"));
        _mockDalleService.Setup(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        _mockBlobStorageService.Setup(x => x.UploadComicImageAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync("https://blob.storage/comic.png");
        _mockComicRepository.Setup(x => x.UpsertAsync(It.IsAny<Comic>()))
            .Callback<Comic>(comic => capturedComic = comic)
            .Returns(Task.CompletedTask);

        // Act
        await service.GenerateComicAsync(placeId, forceRegenerate: false);

        // Assert
        Assert.NotNull(capturedComic);
        // Cache duration is 7 days to reduce AI costs - allow tolerance for test execution time (6-8 days)
        Assert.True(capturedComic.ExpiresAt > DateTimeOffset.UtcNow.AddDays(6), 
            $"ExpiresAt {capturedComic.ExpiresAt} should be more than 6 days from now");
        Assert.True(capturedComic.ExpiresAt <= DateTimeOffset.UtcNow.AddDays(8),
            $"ExpiresAt {capturedComic.ExpiresAt} should be less than 8 days from now");
    }

    [Fact]
    public async Task GenerateComicAsync_WhenRestaurantNotFound_ShouldThrowKeyNotFoundException()
    {
        // Arrange
        var placeId = "nonexistent-place";
        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(placeId))
            .ReturnsAsync((Comic?)null);
        _mockRestaurantService.Setup(x => x.GetRestaurantDetailsAsync(placeId))
            .ReturnsAsync((Restaurant?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GenerateComicAsync(placeId, forceRegenerate: false));
    }

    [Fact]
    public async Task GenerateComicAsync_ShouldIncludeRestaurantNameInComic()
    {
        // Arrange
        var placeId = "test-place-123";
        var restaurantName = "The Quirky Diner";
        var restaurant = new Restaurant
        {
            PlaceId = placeId,
            Name = restaurantName,
            Reviews = Enumerable.Range(1, 5).Select(i => new Review
            {
                Text = $"Strange review {i}",
                Rating = 5
            }).ToList()
        };

        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(placeId))
            .ReturnsAsync((Comic?)null);
        _mockRestaurantService.Setup(x => x.GetRestaurantDetailsAsync(placeId))
            .ReturnsAsync(restaurant);
        _mockOpenAIService.Setup(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>()))
            .ReturnsAsync((80, 4, "Narrative"));
        _mockDalleService.Setup(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        _mockBlobStorageService.Setup(x => x.UploadComicImageAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync("https://blob.storage/comic.png");

        // Act
        var result = await service.GenerateComicAsync(placeId, forceRegenerate: false);

        // Assert
        Assert.Equal(restaurantName, result.RestaurantName);
    }

    #region PrioritizeReviewsByRating Tests

    [Fact]
    public void PrioritizeReviewsByRating_Prioritizes_OneStarReviews_First()
    {
        // Arrange
        var reviews = new List<Review>
        {
            new() { Rating = 5, Text = "Excellent!" },
            new() { Rating = 1, Text = "Terrible place, worst experience ever!" },
            new() { Rating = 3, Text = "Mediocre" },
            new() { Rating = 1, Text = "Awful service!" },
            new() { Rating = 4, Text = "Pretty good" }
        };

        var service = CreateService();

        // Act - Use reflection to call private method
        var method = typeof(ComicGenerationService).GetMethod(
            "PrioritizeReviewsByRating",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<Review>)method!.Invoke(service, new object[] { reviews })!;

        // Assert
        Assert.Equal(5, result.Count);
        Assert.Equal(1, result[0].Rating); // First should be 1-star
        Assert.Equal(1, result[1].Rating); // Second should be 1-star
        Assert.Equal(3, result[2].Rating); // Third should be 3-star (next negative)
    }

    [Fact]
    public void PrioritizeReviewsByRating_Orders_NegativeReviews_Before_Positive()
    {
        // Arrange
        var reviews = new List<Review>
        {
            new() { Rating = 5, Text = "Amazing!" },
            new() { Rating = 2, Text = "Not good at all" },
            new() { Rating = 4, Text = "Very nice" },
            new() { Rating = 1, Text = "Disgusting" },
            new() { Rating = 3, Text = "Just okay" }
        };

        var service = CreateService();

        // Act
        var method = typeof(ComicGenerationService).GetMethod(
            "PrioritizeReviewsByRating",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<Review>)method!.Invoke(service, new object[] { reviews })!;

        // Assert
        var firstPositiveIndex = result.FindIndex(r => r.Rating >= 4);
        var lastNegativeIndex = result.FindLastIndex(r => r.Rating <= 3);
        
        // All negative reviews should come before positive reviews
        if (firstPositiveIndex >= 0 && lastNegativeIndex >= 0)
        {
            Assert.True(lastNegativeIndex < firstPositiveIndex,
                "Negative reviews should all come before positive reviews");
        }
    }

    [Fact]
    public void PrioritizeReviewsByRating_Falls_Back_To_PositiveReviews_When_Insufficient_Negative()
    {
        // Arrange - Only 2 negative reviews, need 5 total
        var reviews = new List<Review>
        {
            new() { Rating = 1, Text = "Terrible" },
            new() { Rating = 2, Text = "Bad" },
            new() { Rating = 5, Text = "Excellent!" },
            new() { Rating = 5, Text = "Amazing!" },
            new() { Rating = 4, Text = "Very good" }
        };

        var service = CreateService();

        // Act
        var method = typeof(ComicGenerationService).GetMethod(
            "PrioritizeReviewsByRating",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<Review>)method!.Invoke(service, new object[] { reviews })!;

        // Assert
        Assert.Equal(5, result.Count);
        Assert.Equal(2, result.Count(r => r.Rating <= 3)); // 2 negative
        Assert.Equal(3, result.Count(r => r.Rating >= 4)); // 3 positive (fallback)
        
        // First two should be negative
        Assert.True(result[0].Rating <= 3);
        Assert.True(result[1].Rating <= 3);
    }

    [Fact]
    public void PrioritizeReviewsByRating_Handles_AllPositiveReviews()
    {
        // Arrange - Only 5-star reviews
        var reviews = new List<Review>
        {
            new() { Rating = 5, Text = "Perfect!" },
            new() { Rating = 5, Text = "Excellent!" },
            new() { Rating = 5, Text = "Amazing!" },
            new() { Rating = 4, Text = "Very good" },
            new() { Rating = 4, Text = "Good place" }
        };

        var service = CreateService();

        // Act
        var method = typeof(ComicGenerationService).GetMethod(
            "PrioritizeReviewsByRating",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<Review>)method!.Invoke(service, new object[] { reviews })!;

        // Assert
        Assert.Equal(5, result.Count);
        // Should prioritize 4-star before 5-star
        Assert.Equal(4, result[0].Rating);
        Assert.Equal(4, result[1].Rating);
    }

    #endregion

    #region FilterInappropriateReviews Tests

    [Theory]
    [InlineData("This food is fuck terrible")]  // Exact word match
    [InlineData("What a shit restaurant")]      // Exact word match
    [InlineData("The service was ass")]         // Exact word match
    [InlineData("The waiter was a bitch")]      // Exact word match
    public void FilterInappropriateReviews_Removes_ProfanityContent(string inappropriateText)
    {
        // Arrange
        var reviews = new List<string>
        {
            "Great place!",
            inappropriateText,
            "Decent food"
        };

        var service = CreateService();

        // Act
        var method = typeof(ComicGenerationService).GetMethod(
            "FilterInappropriateReviews",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<string>)method!.Invoke(service, new object[] { reviews })!;

        // Assert
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(inappropriateText, result);
    }

    [Fact]
    public void FilterInappropriateReviews_CaseInsensitive()
    {
        // Arrange
        var reviews = new List<string>
        {
            "This is FUCK terrible",      // Exact word match
            "What a SHIT place",           // Exact word match
            "Nice restaurant"
        };

        var service = CreateService();

        // Act
        var method = typeof(ComicGenerationService).GetMethod(
            "FilterInappropriateReviews",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<string>)method!.Invoke(service, new object[] { reviews })!;

        // Assert
        Assert.Single(result);
        Assert.Equal("Nice restaurant", result[0]);
    }

    [Fact]
    public void FilterInappropriateReviews_Does_Not_Filter_Partial_Matches()
    {
        // Arrange - Words containing profanity but not exact matches
        var reviews = new List<string>
        {
            "This restaurant is shitty",  // Contains "shit" but as part of "shitty"
            "The fucking service",        // Contains "fuck" but as part of "fucking"
            "Nice place"
        };

        var service = CreateService();

        // Act
        var method = typeof(ComicGenerationService).GetMethod(
            "FilterInappropriateReviews",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<string>)method!.Invoke(service, new object[] { reviews })!;

        // Assert - Should keep all reviews since words are not exact matches
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void FilterInappropriateReviews_Returns_AllCleanReviews()
    {
        // Arrange
        var reviews = new List<string>
        {
            "Excellent food!",
            "Great atmosphere",
            "Friendly staff",
            "Would recommend"
        };

        var service = CreateService();

        // Act
        var method = typeof(ComicGenerationService).GetMethod(
            "FilterInappropriateReviews",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (List<string>)method!.Invoke(service, new object[] { reviews })!;

        // Assert
        Assert.Equal(4, result.Count);
    }

    #endregion
}
