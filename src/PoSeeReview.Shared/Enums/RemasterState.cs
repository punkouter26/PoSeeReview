namespace PoSeeReview.Shared.Enums;

/// <summary>
/// Where a comic stands in draft-then-remaster: drawn cheaply first, redrawn by the premium image
/// model once it is popular enough to be worth it. A remaster is attempted once — a declined or
/// failed one is never retried, because a failed image call may already have been billed.
/// </summary>
public enum RemasterState
{
    /// <summary>Not remastered (yet). Also every comic drawn before remastering existed.</summary>
    None = 0,

    /// <summary>Redrawn by the remaster model from the same scenes and captions.</summary>
    Remastered = 1,

    /// <summary>The remaster model refused the scenes; the draft stays.</summary>
    Declined = 2,

    /// <summary>The remaster call failed; the draft stays.</summary>
    Failed = 3
}
