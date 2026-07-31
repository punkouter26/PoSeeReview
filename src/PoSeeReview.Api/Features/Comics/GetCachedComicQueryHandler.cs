using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Api.Features.Comics;

public class GetCachedComicQueryHandler(IComicGenerationService comicGenerationService)
{
    public Task<Comic?> ExecuteAsync(PlaceId placeId, CancellationToken cancellationToken)
    {
        return comicGenerationService.GetCachedComicAsync(placeId, cancellationToken);
    }
}
