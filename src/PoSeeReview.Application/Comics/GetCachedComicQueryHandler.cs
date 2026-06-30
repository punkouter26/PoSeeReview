using PoSeeReview.Core.Entities;
using PoSeeReview.Core.Interfaces;

namespace PoSeeReview.Application.Comics;

public class GetCachedComicQueryHandler(IComicGenerationService comicGenerationService)
{
    public Task<Comic?> ExecuteAsync(string placeId, CancellationToken cancellationToken)
    {
        return comicGenerationService.GetCachedComicAsync(placeId, cancellationToken);
    }
}
