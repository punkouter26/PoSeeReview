using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Po.SeeReview.Application.Abstractions;
using Po.SeeReview.Application.Comics;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;

namespace Po.SeeReview.UnitTests.Application;

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
            .Setup(x => x.GenerateComicAsync("place-1", false, default))
            .ReturnsAsync(expected);
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(default(string)!);

        var result = await _sut.ExecuteAsync("place-1", false, default);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUserIdSet_UpsertsPersistsUser()
    {
        var comic = MakeComic("place-2");
        _generationServiceMock
            .Setup(x => x.GenerateComicAsync("place-2", false, default))
            .ReturnsAsync(comic);
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns("ANON123456");

        await _sut.ExecuteAsync("place-2", false, default);

        Assert.Equal("ANON123456", comic.RequestedByUserId);
        _repositoryMock.Verify(x => x.UpsertAsync(comic), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUserIdNull_DoesNotUpsert()
    {
        var comic = MakeComic("place-3");
        _generationServiceMock
            .Setup(x => x.GenerateComicAsync("place-3", false, default))
            .ReturnsAsync(comic);
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(default(string)!);

        await _sut.ExecuteAsync("place-3", false, default);

        _repositoryMock.Verify(x => x.UpsertAsync(It.IsAny<Comic>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequestedByUserIdAlreadyMatches_DoesNotUpsert()
    {
        var comic = MakeComic("place-4", requestedByUserId: "ANON111111");
        _generationServiceMock
            .Setup(x => x.GenerateComicAsync("place-4", false, default))
            .ReturnsAsync(comic);
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns("ANON111111");

        await _sut.ExecuteAsync("place-4", false, default);

        _repositoryMock.Verify(x => x.UpsertAsync(It.IsAny<Comic>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PassesForceRegenerateToService()
    {
        var comic = MakeComic("place-5");
        _generationServiceMock
            .Setup(x => x.GenerateComicAsync("place-5", true, default))
            .ReturnsAsync(comic);
        _identityAccessorMock.Setup(x => x.GetCurrentUserId()).Returns(default(string)!);

        await _sut.ExecuteAsync("place-5", true, default);

        _generationServiceMock.Verify(x => x.GenerateComicAsync("place-5", true, default), Times.Once);
    }

    private static Comic MakeComic(string placeId, string? requestedByUserId = null) =>
        new() { Id = Guid.NewGuid().ToString(), PlaceId = placeId, RequestedByUserId = requestedByUserId ?? string.Empty };
}
