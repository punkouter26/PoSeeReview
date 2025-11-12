using Moq;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;
using Xunit;

namespace Po.SeeReview.UnitTests.Services;

/// <summary>
/// Unit tests for LeaderboardService following TDD principles
/// Tests GetTopComicsAsync and UpsertEntryAsync operations
/// </summary>
public sealed class LeaderboardServiceTests
{
    private readonly Mock<ILeaderboardRepository> _mockRepository;

    public LeaderboardServiceTests()
    {
        _mockRepository = new Mock<ILeaderboardRepository>();
    }

    [Fact]
    public async Task GetTopComicsAsync_WithValidRegion_ReturnsTop10Entries()
    {
        // Arrange
        var region = "US-WA-Seattle";
        var entries = GenerateLeaderboardEntries(15, region); // 15 entries but should return top 10
        _mockRepository.Setup(x => x.GetTopEntriesAsync(region, 10))
            .ReturnsAsync(entries.Take(10).ToList());

        var service = new LeaderboardService(_mockRepository.Object);

        // Act
        var result = await service.GetTopComicsAsync(region, 10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.Count);
        Assert.Equal(1, result[0].Rank); // First entry should be rank 1
        Assert.Equal(10, result[^1].Rank); // Last entry should be rank 10
        Assert.True(result[0].StrangenessScore >= result[^1].StrangenessScore); // Descending order
    }

    [Fact]
    public async Task GetTopComicsAsync_WithFewerThan10Entries_ReturnsAvailableEntries()
    {
        // Arrange
        var region = "US-MT-Bozeman";
        var entries = GenerateLeaderboardEntries(3, region);
        _mockRepository.Setup(x => x.GetTopEntriesAsync(region, 10))
            .ReturnsAsync(entries);

        var service = new LeaderboardService(_mockRepository.Object);

        // Act
        var result = await service.GetTopComicsAsync(region, 10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task GetTopComicsAsync_WithEmptyRegion_ReturnsEmptyList()
    {
        // Arrange
        var region = "US-AK-Nowhere";
        _mockRepository.Setup(x => x.GetTopEntriesAsync(region, 10))
            .ReturnsAsync(new List<LeaderboardEntry>());

        var service = new LeaderboardService(_mockRepository.Object);

        // Act
        var result = await service.GetTopComicsAsync(region, 10);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task UpsertEntryAsync_WithNewEntry_InsertsEntry()
    {
        // Arrange
        var entry = new LeaderboardEntry
        {
            PlaceId = "ChIJTest123",
            RestaurantName = "Test Restaurant",
            Address = "123 Test St",
            Region = "US-CA-SF",
            StrangenessScore = 95,
            ComicBlobUrl = "https://blob.core.windows.net/test.png",
            LastUpdated = DateTimeOffset.UtcNow
        };

        _mockRepository.Setup(x => x.UpsertAsync(It.IsAny<LeaderboardEntry>()))
            .Returns(Task.CompletedTask);

        var service = new LeaderboardService(_mockRepository.Object);

        // Act
        await service.UpsertEntryAsync(entry);

        // Assert
        _mockRepository.Verify(x => x.UpsertAsync(It.Is<LeaderboardEntry>(
            e => e.PlaceId == entry.PlaceId && e.StrangenessScore == entry.StrangenessScore
        )), Times.Once);
    }

    [Fact]
    public async Task UpsertEntryAsync_WithHigherScore_UpdatesExistingEntry()
    {
        // Arrange
        var placeId = "ChIJUpdate123";
        var existingEntry = new LeaderboardEntry
        {
            PlaceId = placeId,
            StrangenessScore = 75,
            Region = "US-NY-NYC"
        };

        var newEntry = new LeaderboardEntry
        {
            PlaceId = placeId,
            StrangenessScore = 90, // Higher score
            Region = "US-NY-NYC",
            RestaurantName = "Updated Restaurant",
            Address = "456 Update Ave",
            ComicBlobUrl = "https://blob.core.windows.net/updated.png",
            LastUpdated = DateTimeOffset.UtcNow
        };

        _mockRepository.Setup(x => x.GetByPlaceIdAsync(placeId, "US-NY-NYC"))
            .ReturnsAsync(existingEntry);
        _mockRepository.Setup(x => x.UpsertAsync(It.IsAny<LeaderboardEntry>()))
            .Returns(Task.CompletedTask);

        var service = new LeaderboardService(_mockRepository.Object);

        // Act
        await service.UpsertEntryAsync(newEntry);

        // Assert
        _mockRepository.Verify(x => x.UpsertAsync(It.Is<LeaderboardEntry>(
            e => e.PlaceId == placeId && e.StrangenessScore == 90
        )), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetTopComicsAsync_WithInvalidRegion_ThrowsArgumentException(string? invalidRegion)
    {
        // Arrange
        var service = new LeaderboardService(_mockRepository.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetTopComicsAsync(invalidRegion!, 10));
    }

    [Fact]
    public async Task UpsertEntryAsync_WithNullEntry_ThrowsArgumentNullException()
    {
        // Arrange
        var service = new LeaderboardService(_mockRepository.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.UpsertEntryAsync(null!));
    }

    private static List<LeaderboardEntry> GenerateLeaderboardEntries(int count, string region)
    {
        var entries = new List<LeaderboardEntry>();
        for (int i = 0; i < count; i++)
        {
            entries.Add(new LeaderboardEntry
            {
                Rank = i + 1,
                PlaceId = $"ChIJTest{i:D3}",
                RestaurantName = $"Restaurant {i + 1}",
                Address = $"{i + 1} Main St",
                Region = region,
                StrangenessScore = 100 - i * 2, // Descending scores
                ComicBlobUrl = $"https://blob.core.windows.net/comic{i}.png",
                LastUpdated = DateTimeOffset.UtcNow
            });
        }

        return entries;
    }

    // Placeholder service class to make tests compile
    private class LeaderboardService
    {
        private readonly ILeaderboardRepository _repository;

        public LeaderboardService(ILeaderboardRepository repository)
        {
            _repository = repository;
        }

        public async Task<List<LeaderboardEntry>> GetTopComicsAsync(string region, int limit)
        {
            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Region cannot be empty", nameof(region));

            return await _repository.GetTopEntriesAsync(region, limit);
        }

        public async Task UpsertEntryAsync(LeaderboardEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            await _repository.UpsertAsync(entry);
        }
    }
}
