namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// What the caller has left to spend on comic generation today. Response of
/// <c>GET /api/comics/budget</c>, and the payload behind a 429 from the generation endpoints.
/// <para>
/// The per-minute rate limiter is partitioned by IP and says nothing about cost over a day; this
/// is the daily cap that actually bounds spend on the paid image model.
/// </para>
/// </summary>
public sealed class GenerationBudgetDto
{
    /// <summary>Comics this principal may still generate today.</summary>
    public int Remaining { get; set; }

    /// <summary>The daily per-user allowance.</summary>
    public int DailyLimit { get; set; }

    /// <summary>Generations this principal has already spent today.</summary>
    public int Used { get; set; }

    /// <summary>UTC instant the daily window rolls over.</summary>
    public DateTimeOffset ResetsAt { get; set; }

    /// <summary>
    /// False when the app-wide daily ceiling is exhausted. Distinct from a personal
    /// <see cref="Remaining"/> of zero, and worded differently to the user: nothing they do
    /// restores it before the reset.
    /// </summary>
    public bool ServiceHasCapacity { get; set; } = true;

    /// <summary>True when this principal may start a generation right now.</summary>
    public bool CanGenerate => Remaining > 0 && ServiceHasCapacity;
}
