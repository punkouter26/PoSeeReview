using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoSeeReview.Api.Features.Comics;
using PoSeeReview.Api.Identity;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Unit.Application;

public class GenerateComicCommandHandlerTests
{
    private readonly Mock<IComicGenerationService> _generationServiceMock = new();
    private readonly Mock<IComicRepository> _repositoryMock = new();
    private readonly Mock<ICurrentRequestIdentityAccessor> _identityAccessorMock = new();
    private readonly GenerateComicCommandHandler _sut;

    public GenerateComicCommandHandlerTests()
    {
        _sut = new GenerateComicCommandHandler(
            _generationServiceMock.Object,
            _repositoryMock.Object,
            _identityAccessorMock.Object,
            NullLogger<GenerateComicCommandHandler>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsComicFromGenerationService()
    {
        var expected = MakeComic("place-1");
        _generationServiceMock
            .Setup(x => x.GenerateComicAsync(PlaceId.From("place-1"), false, default))
            .ReturnsAsync(expected);
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(UserId.Anonymous);

        var result = await _sut.ExecuteAsync(PlaceId.From("place-1"), false, default);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUserIdSet_UpsertsPersistsUser()
    {
        var comic = MakeComic("place-2");
        _generationServiceMock
            .Setup(x => x.GenerateComicAsync(PlaceId.From("place-2"), false, default))
            .ReturnsAsync(comic);
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(UserId.From("ANON123456"));

        await _sut.ExecuteAsync(PlaceId.From("place-2"), false, default);

        Assert.Equal(UserId.From("ANON123456"), comic.RequestedByUserId);
        _repositoryMock.Verify(x => x.UpsertAsync(comic), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUserIdNull_DoesNotUpsert()
    {
        var comic = MakeComic("place-3");
        _generationServiceMock
            .Setup(x => x.GenerateComicAsync(PlaceId.From("place-3"), false, default))
            .ReturnsAsync(comic);
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(UserId.Anonymous);

        await _sut.ExecuteAsync(PlaceId.From("place-3"), false, default);

        _repositoryMock.Verify(x => x.UpsertAsync(It.IsAny<Comic>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequestedByUserIdAlreadyMatches_DoesNotUpsert()
    {
        var comic = MakeComic("place-4", requestedByUserId: "ANON111111");
        _generationServiceMock
            .Setup(x => x.GenerateComicAsync(PlaceId.From("place-4"), false, default))
            .ReturnsAsync(comic);
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(UserId.From("ANON111111"));

        await _sut.ExecuteAsync(PlaceId.From("place-4"), false, default);

        _repositoryMock.Verify(x => x.UpsertAsync(It.IsAny<Comic>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PassesForceRegenerateToService()
    {
        var comic = MakeComic("place-5");
        _generationServiceMock
            .Setup(x => x.GenerateComicAsync(PlaceId.From("place-5"), true, default))
            .ReturnsAsync(comic);
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(UserId.Anonymous);

        await _sut.ExecuteAsync(PlaceId.From("place-5"), true, default);

        _generationServiceMock.Verify(x => x.GenerateComicAsync(PlaceId.From("place-5"), true, default), Times.Once);
    }

    private static Comic MakeComic(string placeId, string? requestedByUserId = null) =>
        new()
        {
            Id = ComicId.New(),
            PlaceId = PlaceId.From(placeId),
            RequestedByUserId = UserId.From(requestedByUserId)
        };
}
