using Microsoft.Extensions.Logging;
using PoSeeReview.Application.Abstractions;
using PoSeeReview.Core.Entities;
using PoSeeReview.Core.Interfaces;

namespace PoSeeReview.Application.Comics;

public class GenerateComicCommandHandler(
    IComicGenerationService comicGenerationService,
    IComicRepository comicRepository,
    ICurrentRequestIdentityAccessor currentRequestIdentityAccessor,
    ILogger<GenerateComicCommandHandler> logger)
{
    public async Task<Comic> ExecuteAsync(string placeId, bool forceRegenerate, CancellationToken cancellationToken)
    {
        var comic = await comicGenerationService.GenerateComicAsync(placeId, forceRegenerate, cancellationToken);
        var currentUserId = currentRequestIdentityAccessor.GetCurrentUserId();

        if (!string.IsNullOrWhiteSpace(currentUserId) && comic.RequestedByUserId != currentUserId)
        {
            comic.RequestedByUserId = currentUserId;
            await comicRepository.UpsertAsync(comic);
            logger.LogInformation("Persisted request user {UserId} for comic {ComicId}", currentUserId, comic.Id);
        }

        return comic;
    }
}
