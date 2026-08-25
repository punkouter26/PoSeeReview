using Microsoft.Extensions.Logging;
using PoSeeReview.Api.Identity;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Api.Features.Comics;

public class GenerateComicCommandHandler(
    IComicGenerationService comicGenerationService,
    IComicRepository comicRepository,
    ICurrentRequestIdentityAccessor currentRequestIdentityAccessor,
    ILogger<GenerateComicCommandHandler> logger)
{
    public async Task<Comic> ExecuteAsync(
        PlaceId placeId,
        bool forceRegenerate,
        CancellationToken cancellationToken,
        IProgress<ComicGenerationPhase>? progress = null)
    {
        var comic = await comicGenerationService.GenerateComicAsync(placeId, forceRegenerate, progress, cancellationToken);
        var currentUserId = currentRequestIdentityAccessor.GetCurrentUserId();

        if (!currentUserId.IsAnonymous && comic.RequestedByUserId != currentUserId)
        {
            comic.RequestedByUserId = currentUserId;
            await comicRepository.UpsertAsync(comic);
            logger.LogInformation("Persisted request user {UserId} for comic {ComicId}", currentUserId.Value, comic.Id.Value);
        }

        return comic;
    }
}
