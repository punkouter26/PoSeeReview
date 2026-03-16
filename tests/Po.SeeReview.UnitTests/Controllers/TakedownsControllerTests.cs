using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Shared.Dtos;
using Po.SeeReview.Api.Controllers;
using Xunit;

namespace Po.SeeReview.UnitTests.Controllers;

/// <summary>
/// Unit tests for TakedownsController - covers the data-deletion path
/// that removes comic assets from Table Storage and Blob Storage.
/// </summary>
public sealed class TakedownsControllerTests
{
    private readonly Mock<IComicRepository> _mockComicRepository;
    private readonly Mock<IBlobStorageService> _mockBlobStorageService;
    private readonly Mock<ILeaderboardRepository> _mockLeaderboardRepository;
    private readonly Mock<ILogger<TakedownsController>> _mockLogger;
    private readonly TelemetryClient _telemetryClient;
    private readonly TakedownsController _controller;

    public TakedownsControllerTests()
    {
        _mockComicRepository = new Mock<IComicRepository>();
        _mockBlobStorageService = new Mock<IBlobStorageService>();
        _mockLeaderboardRepository = new Mock<ILeaderboardRepository>();
        _mockLogger = new Mock<ILogger<TakedownsController>>();
        _telemetryClient = new TelemetryClient(new TelemetryConfiguration { DisableTelemetry = true });

        _controller = new TakedownsController(
            _mockComicRepository.Object,
            _mockBlobStorageService.Object,
            _mockLeaderboardRepository.Object,
            _mockLogger.Object,
            _telemetryClient);
    }

    [Fact]
    public async Task SubmitAsync_WhenComicExists_DeletesComicBlobAndLeaderboardEntry()
    {
        // Arrange
        var request = BuildValidRequest("ChIJ-abc123", "US-WA-Seattle");
        var existingComic = new Po.SeeReview.Core.Entities.Comic
        {
            Id = "comic-id-001",
            PlaceId = request.PlaceId
        };

        _mockComicRepository.Setup(r => r.GetByPlaceIdAsync(request.PlaceId))
            .ReturnsAsync(existingComic);

        // Act
        var result = await _controller.SubmitAsync(request, CancellationToken.None);

        // Assert
        Assert.IsType<AcceptedResult>(result);
        _mockComicRepository.Verify(r => r.DeleteAsync(request.PlaceId), Times.Once);
        _mockBlobStorageService.Verify(b => b.DeleteComicImageAsync(existingComic.Id), Times.Once);
        _mockLeaderboardRepository.Verify(l => l.DeleteAsync(request.PlaceId, request.Region), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenNoComicExists_ReturnsAcceptedWithoutDeletingAssets()
    {
        // Arrange
        var request = BuildValidRequest("ChIJ-notfound", "US-CA-SanFrancisco");

        _mockComicRepository.Setup(r => r.GetByPlaceIdAsync(request.PlaceId))
            .ReturnsAsync((Po.SeeReview.Core.Entities.Comic?)null);

        // Act
        var result = await _controller.SubmitAsync(request, CancellationToken.None);

        // Assert – accepted but no delete calls made
        Assert.IsType<AcceptedResult>(result);
        _mockComicRepository.Verify(r => r.DeleteAsync(It.IsAny<string>()), Times.Never);
        _mockBlobStorageService.Verify(b => b.DeleteComicImageAsync(It.IsAny<string>()), Times.Never);
        _mockLeaderboardRepository.Verify(l => l.DeleteAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_WhenComicIdIsEmpty_SkipsBlobDeletion()
    {
        // Arrange – comic exists but blob URL/id was never set (generation failed partway)
        var request = BuildValidRequest("ChIJ-partial", "US-NY-NYC");
        var existingComic = new Po.SeeReview.Core.Entities.Comic
        {
            Id = string.Empty,
            PlaceId = request.PlaceId
        };

        _mockComicRepository.Setup(r => r.GetByPlaceIdAsync(request.PlaceId))
            .ReturnsAsync(existingComic);

        // Act
        var result = await _controller.SubmitAsync(request, CancellationToken.None);

        // Assert – table entry and leaderboard deleted, but blob skipped (no Id)
        Assert.IsType<AcceptedResult>(result);
        _mockComicRepository.Verify(r => r.DeleteAsync(request.PlaceId), Times.Once);
        _mockBlobStorageService.Verify(b => b.DeleteComicImageAsync(It.IsAny<string>()), Times.Never);
        _mockLeaderboardRepository.Verify(l => l.DeleteAsync(request.PlaceId, request.Region), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_AlwaysReturns202Accepted()
    {
        // Arrange – regardless of whether comic exists, we return 202
        var request = BuildValidRequest("ChIJ-any", "US-WA-Seattle");
        _mockComicRepository.Setup(r => r.GetByPlaceIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Po.SeeReview.Core.Entities.Comic?)null);

        // Act
        var result = await _controller.SubmitAsync(request, CancellationToken.None);

        // Assert
        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.NotNull(accepted.Value);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private static TakedownRequestDto BuildValidRequest(string placeId, string region) => new()
    {
        PlaceId = placeId,
        ContactEmail = "owner@restaurant.com",
        RequesterName = "Restaurant Owner",
        Region = region,
        Reason = "We do not consent to this content appearing on your platform."
    };
}
