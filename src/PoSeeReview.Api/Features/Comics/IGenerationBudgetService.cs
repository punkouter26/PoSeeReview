using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>Why a generation attempt was refused, or that it was allowed.</summary>
public enum BudgetDecision
{
    /// <summary>The caller may generate, and one unit has been charged.</summary>
    Allowed = 0,

    /// <summary>This principal has spent their daily allowance.</summary>
    UserExhausted = 1,

    /// <summary>The app-wide daily ceiling is reached; no caller may generate.</summary>
    ServiceExhausted = 2
}

/// <summary>Outcome of a budget reservation, carrying the snapshot the client renders.</summary>
/// <param name="Decision">Whether the caller may proceed.</param>
/// <param name="Budget">The caller's budget as it stands after the attempt.</param>
public readonly record struct BudgetReservation(BudgetDecision Decision, GenerationBudgetDto Budget)
{
    /// <summary>True when the caller may run the paid pipeline.</summary>
    public bool IsAllowed => Decision == BudgetDecision.Allowed;
}

/// <summary>
/// Daily spend guard for the paid generation pipeline. Owned by the Comics slice because the
/// spend is the Comics slice's (NET_RULES 2.2 — slices do not reference each other).
/// </summary>
public interface IGenerationBudgetService
{
    /// <summary>Reads the current principal's budget without charging it.</summary>
    Task<GenerationBudgetDto> GetBudgetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Charges one generation to the current principal and the service total, or refuses.
    /// Call before the pipeline runs; refund with <see cref="ReleaseAsync"/> if no paid work
    /// happened after all.
    /// </summary>
    Task<BudgetReservation> TryReserveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a previously reserved unit — used when the pipeline turned out to serve a cached
    /// comic and spent nothing. Best-effort: a failed refund must never fail the request that
    /// already succeeded.
    /// </summary>
    Task ReleaseAsync(CancellationToken cancellationToken = default);
}
