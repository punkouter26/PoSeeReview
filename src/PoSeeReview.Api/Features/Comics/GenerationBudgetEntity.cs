using Azure;
using Azure.Data.Tables;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// One day's generation counter for a single principal, or for the service as a whole.
/// <para>
/// PartitionKey: <c>BUDGET_{yyyy-MM-dd}</c> — one partition per UTC day, so a day's rows are a
/// single point query and old days can be dropped wholesale.
/// RowKey: the sanitized principal key, or <see cref="ServiceRowKey"/> for the app-wide row.
/// </para>
/// </summary>
public class GenerationBudgetEntity : ITableEntity
{
    /// <summary>Row holding the app-wide daily total. Not a legal principal key, so it cannot collide.</summary>
    public const string ServiceRowKey = "__service__";

    private const string PartitionKeyPrefix = "BUDGET";

    /// <summary>Builds the partition key for a UTC day.</summary>
    public static string PartitionKeyFor(DateOnly day) => $"{PartitionKeyPrefix}_{day:yyyy-MM-dd}";

    /// <summary>
    /// Makes an arbitrary principal id safe as a RowKey. Table Storage rejects <c>/ \ # ?</c>
    /// and control characters in keys, and an Entra object id is a GUID while a dev-session id
    /// or an IP fallback is not — so nothing may be assumed about the input.
    /// </summary>
    public static string RowKeyFor(string principalKey)
    {
        var sanitized = new string(principalKey
            .Where(c => !char.IsControl(c) && c is not ('/' or '\\' or '#' or '?'))
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>Paid generations charged to this row today.</summary>
    public int Count { get; set; }
}
