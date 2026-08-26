using System.Threading;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PoSeeReview.Api.Features.Comics;
using PoSeeReview.Api.Features.Leaderboard;
using PoSeeReview.Api.Features.Restaurants;
using PoSeeReview.Api.Storage;
using Xunit;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Unit.Services;

/// <summary>
/// Unit tests for ComicGenerationService - orchestrates review analysis, narrative generation, and comic creation
/// </summary>
[Trait("Tier", "Unit")]
[Trait("Suite", "CriticalPath")]
public class ComicGenerationServiceTests
{
    private readonly Mock<IRestaurantService> _mockRestaurantService;
    private readonly Mock<IChatCompletionService> _mockOpenAIService;
    private readonly Mock<IImageGenerationService> _mockImageGenerationService;
    private readonly Mock<IComicTextOverlayService> _mockTextOverlayService;
    private readonly Mock<IBlobStorageService> _mockBlobStorageService;
    private readonly Mock<IComicRepository> _mockComicRepository;
    private readonly Mock<ILeaderboardService> _mockLeaderboardService;
    private readonly Mock<ILogger<ComicGenerationService>> _mockLogger;
    private readonly TelemetryClient _telemetryClient;

    public ComicGenerationServiceTests()
    {
        _mockRestaurantService = new Mock<IRestaurantService>();
        _mockOpenAIService = new Mock<IChatCompletionService>();
        _mockImageGenerationService = new Mock<IImageGenerationService>();
        _mockTextOverlayService = new Mock<IComicTextOverlayService>();
        _mockBlobStorageService = new Mock<IBlobStorageService>();
        _mockComicRepository = new Mock<IComicRepository>();
        _mockLeaderboardService = new Mock<ILeaderboardService>();
        _mockLogger = new Mock<ILogger<ComicGenerationService>>();
        _telemetryClient = new TelemetryClient(new TelemetryConfiguration());
    }

    private ComicGenerationService CreateService(ComicOptions? options = null)
    {
        // Default setup: text overlay returns input bytes unchanged (passthrough)
        _mockTextOverlayService.Setup(x => x.AddTextOverlayAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[] imageBytes, string narrative, int panelCount, CancellationToken ct) => imageBytes);

        return new ComicGenerationService(
            _mockRestaurantService.Object,
            _mockOpenAIService.Object,
            _mockImageGenerationService.Object,
            _mockTextOverlayService.Object,
            _mockBlobStorageService.Object,
            _mockComicRepository.Object,
            _mockLeaderboardService.Object,
            _mockLogger.Object,
            _telemetryClient,
            Options.Create(options ?? new ComicOptions())
        );
    }

    [Fact]
    public async Task GenerateComicAsync_WithValidRestaurant_ShouldReturnCachedComicIfValid()
    {
        // Arrange
        var placeId = "test-place-123";
        var cachedComic = new Comic
        {
            Id = ComicId.New(),
            PlaceId = PlaceId.From(placeId),
            RestaurantName = "Test Restaurant",
            ImageUrl = "https://example.com/comic.png",
            Narrative = "Test narrative",
            StrangenessScore = 85,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(12) // Still valid
        };

        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(PlaceId.From(placeId)))
            .ReturnsAsync(cachedComic);

        // Act
        var result = await service.GenerateComicAsync(PlaceId.From(placeId), forceRegenerate: false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PlaceId.From(placeId), result.PlaceId);
        Assert.Equal(cachedComic.ImageUrl, result.ImageUrl);
        Assert.Equal(ComicCacheState.Cached, result.CacheState);
        _mockImageGenerationService.Verify(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateComicAsync_WithExpiredCache_ShouldRegenerateComic()
    {
        // Arrange
        var placeId = "test-place-123";
        var expiredComic = new Comic
        {
            PlaceId = PlaceId.From(placeId),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) // Expired
        };

        var restaurant = new Restaurant
        {
            PlaceId = PlaceId.From(placeId),
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
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(PlaceId.From(placeId)))
            .ReturnsAsync(expiredComic);
        _mockRestaurantService.Setup(x => x.GetRestaurantByPlaceIdAsync(PlaceId.From(placeId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(restaurant);
        _mockOpenAIService.Setup(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StrangenessAnalysis(75, 3, "A restaurant where waiters dress as dinosaurs and food is served in shoes."));
        _mockImageGenerationService.Setup(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        _mockBlobStorageService.Setup(x => x.UploadComicImageAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync("https://blob.storage/comic.png");

        // Act
        var result = await service.GenerateComicAsync(PlaceId.From(placeId), forceRegenerate: false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ComicCacheState.Generated, result.CacheState);
        _mockImageGenerationService.Verify(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockComicRepository.Verify(x => x.UpsertAsync(It.IsAny<Comic>()), Times.Once);
    }

    [Fact]
    public async Task GenerateComicAsync_WithForceRegenerate_ShouldAlwaysGenerateNewComic()
    {
        // Arrange
        var placeId = "test-place-123";
        var cachedComic = new Comic
        {
            PlaceId = PlaceId.From(placeId),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(12) // Still valid
        };

        var restaurant = new Restaurant
        {
            PlaceId = PlaceId.From(placeId),
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
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(PlaceId.From(placeId)))
            .ReturnsAsync(cachedComic);
        _mockRestaurantService.Setup(x => x.GetRestaurantByPlaceIdAsync(PlaceId.From(placeId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(restaurant);
        _mockOpenAIService.Setup(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StrangenessAnalysis(75, 3, "Test narrative"));
        _mockImageGenerationService.Setup(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        _mockBlobStorageService.Setup(x => x.UploadComicImageAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync("https://blob.storage/comic.png");

        // Act
        var result = await service.GenerateComicAsync(PlaceId.From(placeId), forceRegenerate: true);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ComicCacheState.Generated, result.CacheState);
        _mockImageGenerationService.Verify(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateComicAsync_WithFewerReviewsThanConfiguredMinimum_ShouldThrowInsufficientReviews()
    {
        // Arrange — the minimum is configuration, so the test must set it rather than lean on
        // the default. This previously asserted that ONE review was insufficient, which stopped
        // being true when MinimumReviewsRequired dropped to 1; the test then sailed past the
        // guard and failed with a NullReferenceException from deeper in the pipeline.
        var placeId = "test-place-123";
        var restaurant = new Restaurant
        {
            PlaceId = PlaceId.From(placeId),
            Name = "Test Restaurant",
            Reviews = new List<Review>
            {
                new Review { Text = "Only one review", Rating = 5 }
            }
        };

        var service = CreateService(new ComicOptions { MinimumReviewsRequired = 2 });
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(PlaceId.From(placeId)))
            .ReturnsAsync((Comic?)null);
        _mockRestaurantService.Setup(x => x.GetRestaurantByPlaceIdAsync(PlaceId.From(placeId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(restaurant);

        // Act & Assert — InsufficientReviewsException derives from InvalidOperationException
        await Assert.ThrowsAsync<InsufficientReviewsException>(() =>
            service.GenerateComicAsync(PlaceId.From(placeId), forceRegenerate: false));
    }

    [Fact]
    public async Task GenerateComicAsync_WhenAnalyzerReturnsEmptyNarrative_ShouldThrowInsteadOfNullReferencing()
    {
        // Arrange — an empty narrative is a plausible AI response, not a bug in our code, and
        // must surface as a handled 400 rather than a NullReferenceException the user reads as
        // "Object reference not set to an instance of an object".
        var placeId = "test-place-123";
        var restaurant = new Restaurant
        {
            PlaceId = PlaceId.From(placeId),
            Name = "Test Restaurant",
            Reviews = new List<Review> { new Review { Text = "A review", Rating = 1 } }
        };

        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(PlaceId.From(placeId)))
            .ReturnsAsync((Comic?)null);
        _mockRestaurantService.Setup(x => x.GetRestaurantByPlaceIdAsync(PlaceId.From(placeId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(restaurant);
        _mockOpenAIService.Setup(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StrangenessAnalysis(72, 2, string.Empty));

        // Act & Assert
        await Assert.ThrowsAsync<InsufficientReviewsException>(() =>
            service.GenerateComicAsync(PlaceId.From(placeId), forceRegenerate: false));

        _mockImageGenerationService.Verify(
            x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateComicAsync_WithMinimumReviews_ShouldSucceed()
    {
        // Arrange
        var placeId = "test-place-123";
        var restaurant = new Restaurant
        {
            PlaceId = PlaceId.From(placeId),
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
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(PlaceId.From(placeId)))
            .ReturnsAsync((Comic?)null);
        _mockRestaurantService.Setup(x => x.GetRestaurantByPlaceIdAsync(PlaceId.From(placeId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(restaurant);
        _mockOpenAIService.Setup(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StrangenessAnalysis(50, 2, "Narrative"));
        _mockImageGenerationService.Setup(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        _mockBlobStorageService.Setup(x => x.UploadComicImageAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync("https://blob.storage/comic.png");

        // Act
        var result = await service.GenerateComicAsync(PlaceId.From(placeId), forceRegenerate: false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(PlaceId.From(placeId), result.PlaceId);
    }

    [Fact]
    public async Task GenerateComicAsync_ShouldSet7DayCacheExpiration()
    {
        // Arrange
        var placeId = "test-place-123";
        var restaurant = new Restaurant
        {
            PlaceId = PlaceId.From(placeId),
            Name = "Test Restaurant",
            Reviews = Enumerable.Range(1, 10).Select(i => new Review
            {
                Text = $"Review {i}",
                Rating = 5
            }).ToList()
        };

        Comic? capturedComic = null;
        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(PlaceId.From(placeId)))
            .ReturnsAsync((Comic?)null);
        _mockRestaurantService.Setup(x => x.GetRestaurantByPlaceIdAsync(PlaceId.From(placeId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(restaurant);
        _mockOpenAIService.Setup(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StrangenessAnalysis(70, 3, "Narrative"));
        _mockImageGenerationService.Setup(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        _mockBlobStorageService.Setup(x => x.UploadComicImageAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync("https://blob.storage/comic.png");
        _mockComicRepository.Setup(x => x.UpsertAsync(It.IsAny<Comic>()))
            .Callback<Comic>(comic => capturedComic = comic)
            .Returns(Task.CompletedTask);

        // Act
        await service.GenerateComicAsync(PlaceId.From(placeId), forceRegenerate: false);

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
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(PlaceId.From(placeId)))
            .ReturnsAsync((Comic?)null);
        _mockRestaurantService.Setup(x => x.GetRestaurantByPlaceIdAsync(PlaceId.From(placeId), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("not found"));

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GenerateComicAsync(PlaceId.From(placeId), forceRegenerate: false));
    }

    [Fact]
    public async Task GenerateComicAsync_ShouldIncludeRestaurantNameInComic()
    {
        // Arrange
        var placeId = "test-place-123";
        var restaurantName = "The Quirky Diner";
        var restaurant = new Restaurant
        {
            PlaceId = PlaceId.From(placeId),
            Name = restaurantName,
            Reviews = Enumerable.Range(1, 5).Select(i => new Review
            {
                Text = $"Strange review {i}",
                Rating = 5
            }).ToList()
        };

        var service = CreateService();
        _mockComicRepository.Setup(x => x.GetByPlaceIdAsync(PlaceId.From(placeId)))
            .ReturnsAsync((Comic?)null);
        _mockRestaurantService.Setup(x => x.GetRestaurantByPlaceIdAsync(PlaceId.From(placeId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(restaurant);
        _mockOpenAIService.Setup(x => x.AnalyzeStrangenessAsync(It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StrangenessAnalysis(80, 4, "Narrative"));
        _mockImageGenerationService.Setup(x => x.GenerateComicImageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 1, 2, 3, 4 });
        _mockBlobStorageService.Setup(x => x.UploadComicImageAsync(It.IsAny<string>(), It.IsAny<byte[]>()))
            .ReturnsAsync("https://blob.storage/comic.png");

        // Act
        var result = await service.GenerateComicAsync(PlaceId.From(placeId), forceRegenerate: false);

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
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
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
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
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
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = (List<string>)method!.Invoke(service, new object[] { reviews })!;

        // Assert - Should keep all reviews since words are not exact matches
        Assert.Equal(3, result.Count);
    }

    #endregion
}
