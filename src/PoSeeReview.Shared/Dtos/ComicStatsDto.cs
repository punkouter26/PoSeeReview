namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// Context for a comic's strangeness score — what the number actually means relative to the
/// region it was scored in. Response of <c>GET /api/comics/{placeId}/stats</c>.
/// <para>
/// Everything here is <em>derived</em> from scores this app computed. It deliberately carries no
/// review text: strings rendered as quotations from real reviewers about a named restaurant are
/// a defamation problem when the model invents one, which is why the receipts vertical was
/// pruned. Percentiles cannot be fabricated in that way.
/// </para>
/// </summary>
public sealed class ComicStatsDto
{
    /// <summary>Google Maps place identifier this score belongs to.</summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>Region the comparison was drawn from (e.g. <c>US</c>).</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>The comic's own strangeness score (0–100).</summary>
    public int StrangenessScore { get; set; }

    /// <summary>
    /// 1-based position among ranked comics in <see cref="Region"/>, or <c>null</c> when this
    /// comic scored below the leaderboard's admission threshold and was never ranked.
    /// </summary>
    public int? RegionRank { get; set; }

    /// <summary>How many ranked comics the comparison was made against.</summary>
    public int RegionSampleSize { get; set; }

    /// <summary>
    /// Share of the regional sample this comic beats, 0–100. Meaningless below a handful of
    /// samples, which is what <see cref="HasMeaningfulSample"/> is for.
    /// </summary>
    public int Percentile { get; set; }

    /// <summary>Median score across the regional sample, for a "typical here" comparison.</summary>
    public double RegionMedianScore { get; set; }

    /// <summary>Highest score currently standing in the region.</summary>
    public double RegionTopScore { get; set; }

    /// <summary>
    /// False when the region holds too few ranked comics for a percentile to say anything.
    /// The client shows the raw score alone rather than "weirder than 100% of 1 restaurant".
    /// </summary>
    public bool HasMeaningfulSample { get; set; }

    /// <summary>Minimum ranked comics before a percentile is worth showing a user.</summary>
    public const int MeaningfulSampleThreshold = 5;
}
