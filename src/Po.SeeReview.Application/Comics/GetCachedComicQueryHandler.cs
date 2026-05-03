using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;

namespace Po.SeeReview.Application.Comics;

public class GetCachedComicQueryHandler(IComicGenerationService comicGenerationService)
{
    public Task<Comic?> ExecuteAsync(string placeId, CancellationToken cancellationToken)
    {
        return comicGenerationService.GetCachedComicAsync(placeId, cancellationToken);
    }
}
