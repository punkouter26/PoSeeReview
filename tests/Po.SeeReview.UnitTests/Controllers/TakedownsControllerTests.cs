using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Po.SeeReview.Api.Features.Takedowns;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Shared.Dtos;
using Po.SeeReview.Shared.Validation;
using Xunit;

namespace Po.SeeReview.UnitTests.Controllers;

/// <summary>
/// Unit tests for the Takedowns slice endpoint — covers the data-deletion path
/// that removes comic assets from Table Storage and Blob Storage.
/// </summary>
[Trait("Tier", "Unit")]
[Trait("Suite", "CriticalPath")]
public sealed class TakedownsControllerTests
{
    private readonly Mock<IComicRepository> _mockComicRepository = new();
    private readonly Mock<IBlobStorageService> _mockBlobStorageService = new();
    private readonly Mock<ILeaderboardRepository> _mockLeaderboardRepository = new();
    private readonly Mock<ILogger<TakedownRequestDto>> _mockLogger = new();
    private readonly TelemetryClient _telemetryClient = new(new TelemetryConfiguration { DisableTelemetry = true });

    private Task<IResult> SubmitAsync(TakedownRequestDto request) =>
        TakedownsEndpoints.SubmitAsync(
            request,
            new TakedownRequestValidator(),
            _mockComicRepository.Object,
            _mockBlobStorageService.Object,
            _mockLeaderboardRepository.Object,
            _mockLogger.Object,
            _telemetryClient,
            CancellationToken.None);

    private static void AssertAccepted(IResult result) =>
        Assert.Equal(StatusCodes.Status202Accepted, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

    [Fact]
    public async Task SubmitAsync_WhenComicExists_DeletesComicBlobAndLeaderboardEntry()
    {
        var request = BuildValidRequest("ChIJ-abc123", "US-WA-SEATTLE");
        var existingComic = new Po.SeeReview.Core.Entities.Comic { Id = "comic-id-001", PlaceId = request.PlaceId };

        _mockComicRepository.Setup(r => r.GetByPlaceIdAsync(request.PlaceId)).ReturnsAsync(existingComic);

        var result = await SubmitAsync(request);

        AssertAccepted(result);
        _mockComicRepository.Verify(r => r.DeleteAsync(request.PlaceId), Times.Once);
        _mockBlobStorageService.Verify(b => b.DeleteComicImageAsync(existingComic.Id), Times.Once);
        _mockLeaderboardRepository.Verify(l => l.DeleteAsync(request.PlaceId, request.Region), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenNoComicExists_ReturnsAcceptedWithoutDeletingAssets()
    {
        var request = BuildValidRequest("ChIJ-notfound", "US-CA-SANFRANCISCO");
        _mockComicRepository.Setup(r => r.GetByPlaceIdAsync(request.PlaceId))
            .ReturnsAsync((Po.SeeReview.Core.Entities.Comic?)null);

        var result = await SubmitAsync(request);

        AssertAccepted(result);
        _mockComicRepository.Verify(r => r.DeleteAsync(It.IsAny<string>()), Times.Never);
        _mockBlobStorageService.Verify(b => b.DeleteComicImageAsync(It.IsAny<string>()), Times.Never);
        _mockLeaderboardRepository.Verify(l => l.DeleteAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_WhenComicIdIsEmpty_SkipsBlobDeletion()
    {
        var request = BuildValidRequest("ChIJ-partial", "US-NY-NYC");
        var existingComic = new Po.SeeReview.Core.Entities.Comic { Id = string.Empty, PlaceId = request.PlaceId };
        _mockComicRepository.Setup(r => r.GetByPlaceIdAsync(request.PlaceId)).ReturnsAsync(existingComic);

        var result = await SubmitAsync(request);

        AssertAccepted(result);
        _mockComicRepository.Verify(r => r.DeleteAsync(request.PlaceId), Times.Once);
        _mockBlobStorageService.Verify(b => b.DeleteComicImageAsync(It.IsAny<string>()), Times.Never);
        _mockLeaderboardRepository.Verify(l => l.DeleteAsync(request.PlaceId, request.Region), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_AlwaysReturns202Accepted()
    {
        var request = BuildValidRequest("ChIJ-any", "US-WA-SEATTLE");
        _mockComicRepository.Setup(r => r.GetByPlaceIdAsync(It.IsAny<string>()))
            .ReturnsAsync((Po.SeeReview.Core.Entities.Comic?)null);

        var result = await SubmitAsync(request);

        AssertAccepted(result);
    }

    private static TakedownRequestDto BuildValidRequest(string placeId, string region) => new()
    {
        PlaceId = placeId,
        ContactEmail = "owner@restaurant.com",
        RequesterName = "Restaurant Owner",
        Region = region,
        Reason = "We do not consent to this content appearing on your platform."
    };
}
