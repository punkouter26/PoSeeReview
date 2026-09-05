using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Turns a bare strangeness score into something a reader can interpret: where it sits among
/// the comics scored in the same region.
/// <para>
/// Reads the leaderboard through <see cref="ILeaderboardRepository"/>, which lives in Shared
/// precisely so one slice can use another's data without referencing the slice (NET_RULES 2.2).
/// </para>
/// <para>
/// Everything returned is derived arithmetic over scores this app computed. Nothing here quotes
/// a review: model-claimed verbatim fragments about a named restaurant were pruned end to end
/// for defamation reasons, and re-adding them would require the verbatim gate back with them.
/// </para>
/// </summary>
public sealed class ComicStatsQueryHandler(
    IComicRepository comicRepository,
    ILeaderboardRepository leaderboardRepository,
    ILogger<ComicStatsQueryHandler> logger)
{
    /// <summary>
    /// How much of the regional board the comparison samples. Matches the repository's own
    /// ceiling, so the sample is "every ranked comic in the region" for any realistic region.
    /// </summary>
    private const int SampleSize = 50;

    /// <summary>
    /// Builds the score context for a place, or <c>null</c> when no comic exists for it.
    /// </summary>
    public async Task<ComicStatsDto?> ExecuteAsync(PlaceId placeId, CancellationToken cancellationToken = default)
    {
        var comic = await comicRepository.GetByPlaceIdAsync(placeId);
        if (comic is null)
        {
            return null;
        }

        // The comic itself does not carry a region; the leaderboard row does. A comic that
        // scored below the admission threshold has no row at all, which is not an error — it
        // simply has no rank, and the client renders the score alone.
        var ownEntry = await leaderboardRepository.GetByPlaceIdAsync(placeId);
        var region = ownEntry?.Region ?? RegionCode.Default;

        var stats = new ComicStatsDto
        {
            PlaceId = placeId.Value,
            Region = region.Value,
            StrangenessScore = comic.StrangenessScore
            // RegionRank is filled in from the sample below, NOT from ownEntry.Rank: the
            // cross-region lookup returns rows with a rank of 0 by design ("recalculated when
            // needed"), and taking it at face value reported every comic as rank 0.
        };

        List<LeaderboardEntry> sample;
        try
        {
            sample = await leaderboardRepository.GetTopEntriesAsync(region, SampleSize);
        }
        catch (Exception ex)
        {
            // Stats decorate the payoff screen. A leaderboard read failure must not take the
            // comic down with it, so the score ships without its context.
            logger.LogWarning(ex, "Could not read the {Region} board for comic stats on {PlaceId}", region, placeId);
            return stats;
        }

        // The repository returns the board already in descending score order (inverted RowKey),
        // so position in this list IS the rank. A place that is on the board but below the
        // sample window keeps a null rank rather than being given a made-up one.
        var position = sample.FindIndex(e => e.PlaceId == placeId);
        stats.RegionRank = position >= 0 ? position + 1 : null;

        var scores = sample.Select(e => e.StrangenessScore).OrderBy(s => s).ToList();

        stats.RegionSampleSize = scores.Count;
        stats.HasMeaningfulSample = scores.Count >= ComicStatsDto.MeaningfulSampleThreshold;

        if (scores.Count == 0)
        {
            return stats;
        }

        stats.RegionTopScore = scores[^1];
        stats.RegionMedianScore = Median(scores);

        // Strictly-less-than, so a place tied with the whole board is not told it beat it.
        var beaten = scores.Count(s => s < comic.StrangenessScore);
        stats.Percentile = (int)Math.Round(beaten * 100.0 / scores.Count, MidpointRounding.AwayFromZero);

        return stats;
    }

    private static double Median(List<double> ascending) =>
        ascending.Count % 2 == 1
            ? ascending[ascending.Count / 2]
            : (ascending[ascending.Count / 2 - 1] + ascending[ascending.Count / 2]) / 2.0;
}
