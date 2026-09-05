using Azure;
using Azure.Data.Tables;

namespace PoSeeReview.Api.Features.Analytics;

/// <summary>
/// One day's count for one funnel step.
/// <para>
/// PartitionKey: <c>FUNNEL_{yyyy-MM-dd}</c> — a day is one partition, so rendering today's
/// funnel is a single partition query and old days drop wholesale.
/// RowKey: the step name, which is drawn from a closed vocabulary
/// (<c>FunnelSteps</c>), so the row space cannot be grown by a client.
/// </para>
/// </summary>
public class FunnelCounterEntity : ITableEntity
{
    private const string PartitionKeyPrefix = "FUNNEL";

    /// <summary>Builds the partition key for a UTC day.</summary>
    public static string PartitionKeyFor(DateOnly day) => $"{PartitionKeyPrefix}_{day:yyyy-MM-dd}";

    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>Times this step was reported today.</summary>
    public int Count { get; set; }

    /// <summary>
    /// Running total of reported durations, kept alongside <see cref="DurationSamples"/> rather
    /// than as a precomputed mean: a mean cannot be merged across concurrent writers, a sum can.
    /// </summary>
    public long DurationSumMs { get; set; }

    /// <summary>How many of the counted events carried a duration.</summary>
    public int DurationSamples { get; set; }
}
