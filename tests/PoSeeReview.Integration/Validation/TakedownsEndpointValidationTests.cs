using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using PoSeeReview.Api.Features.Comics;
using PoSeeReview.Api.Features.Leaderboard;
using PoSeeReview.Api.Features.Restaurants;
using PoSeeReview.Api.Features.Takedowns;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Validation;
using Xunit;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Integration.Validation;

/// <summary>
/// Takedown slice endpoint behaviour, exercised through the real
/// <see cref="TakedownRequestValidator"/>. Lives in the Integration tier because it
/// wires FluentValidation into the request path (directive #4: all FluentValidation
/// tests belong in the Integration tier, not the No-I/O Unit tier).
/// </summary>
[Trait("Tier", "Integration")]
[Trait("Suite", "CriticalPath")]
public sealed class TakedownsEndpointValidationTests
{
    private readonly Mock<IComicRepository> _mockComicRepository = new();
    private readonly Mock<IBlobStorageService> _mockBlobStorageService = new();
    private readonly Mock<ILeaderboardRepository> _mockLeaderboardRepository = new();
    private readonly Mock<IHallOfFameArchive> _mockHallOfFameArchive = new();
    private readonly Mock<ILogger<TakedownRequestDto>> _mockLogger = new();
    private readonly TelemetryClient _telemetryClient = new(new TelemetryConfiguration { DisableTelemetry = true });

    private Task<IResult> SubmitAsync(TakedownRequestDto request) =>
        TakedownsEndpoints.SubmitAsync(
            request,
            new TakedownRequestValidator(),
            _mockComicRepository.Object,
            _mockBlobStorageService.Object,
            _mockLeaderboardRepository.Object,
            _mockHallOfFameArchive.Object,
            _mockLogger.Object,
            _telemetryClient,
            CancellationToken.None);

    private static void AssertAccepted(IResult result) =>
        Assert.Equal(StatusCodes.Status202Accepted, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

    [Fact]
    public async Task SubmitAsync_WhenComicExists_DeletesComicBlobAndLeaderboardEntry()
    {
        var request = BuildValidRequest("ChIJ-abc123", "US-WA-SEATTLE");
        var existingComic = new PoSeeReview.Shared.Contracts.Comic { Id = ComicId.From("comic-id-001"), PlaceId = PlaceId.From(request.PlaceId) };

        _mockComicRepository.Setup(r => r.GetByPlaceIdAsync(PlaceId.From(request.PlaceId))).ReturnsAsync(existingComic);

        var result = await SubmitAsync(request);

        AssertAccepted(result);
        _mockComicRepository.Verify(r => r.DeleteAsync(PlaceId.From(request.PlaceId)), Times.Once);
        _mockBlobStorageService.Verify(b => b.DeleteComicImageAsync(existingComic.Id.Value), Times.Once);
        _mockLeaderboardRepository.Verify(l => l.DeleteAsync(PlaceId.From(request.PlaceId), RegionCode.From(request.Region)), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_WhenNoComicExists_ReturnsAcceptedWithoutDeletingAssets()
    {
        var request = BuildValidRequest("ChIJ-notfound", "US-CA-SANFRANCISCO");
        _mockComicRepository.Setup(r => r.GetByPlaceIdAsync(PlaceId.From(request.PlaceId)))
            .ReturnsAsync((PoSeeReview.Shared.Contracts.Comic?)null);

        var result = await SubmitAsync(request);

        AssertAccepted(result);
        _mockComicRepository.Verify(r => r.DeleteAsync(It.IsAny<PlaceId>()), Times.Never);
        _mockBlobStorageService.Verify(b => b.DeleteComicImageAsync(It.IsAny<string>()), Times.Never);
        _mockLeaderboardRepository.Verify(l => l.DeleteAsync(It.IsAny<PlaceId>(), It.IsAny<RegionCode>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_WhenComicIdIsEmpty_SkipsBlobDeletion()
    {
        var request = BuildValidRequest("ChIJ-partial", "US-NY-NYC");
        var existingComic = new PoSeeReview.Shared.Contracts.Comic { Id = ComicId.Empty, PlaceId = PlaceId.From(request.PlaceId) };
        _mockComicRepository.Setup(r => r.GetByPlaceIdAsync(PlaceId.From(request.PlaceId))).ReturnsAsync(existingComic);

        var result = await SubmitAsync(request);

        AssertAccepted(result);
        _mockComicRepository.Verify(r => r.DeleteAsync(PlaceId.From(request.PlaceId)), Times.Once);
        _mockBlobStorageService.Verify(b => b.DeleteComicImageAsync(It.IsAny<string>()), Times.Never);
        _mockLeaderboardRepository.Verify(l => l.DeleteAsync(PlaceId.From(request.PlaceId), RegionCode.From(request.Region)), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_AlwaysReturns202Accepted()
    {
        var request = BuildValidRequest("ChIJ-any", "US-WA-SEATTLE");
        _mockComicRepository.Setup(r => r.GetByPlaceIdAsync(It.IsAny<PlaceId>()))
            .ReturnsAsync((PoSeeReview.Shared.Contracts.Comic?)null);

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
