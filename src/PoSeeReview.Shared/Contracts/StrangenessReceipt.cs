namespace PoSeeReview.Shared.Contracts;

/// <summary>
/// One piece of evidence behind a comic's strangeness score: the review fragment the analyser
/// weighted, and how many points it contributed. Lives in Shared because <see cref="Comic"/>
/// carries it across the Comics and Takedowns slices (NET_RULES 2.2).
/// </summary>
/// <param name="Quote">
/// A verbatim fragment of a real public review. Never model-authored prose — the generation
/// pipeline drops any receipt whose quote is not present in the reviews that were analysed,
/// because an invented quote attributed to a real restaurant is a defamation risk, not a
/// cosmetic bug.
/// </param>
/// <param name="Points">Points this fragment contributed to the 0-100 score.</param>
public sealed record StrangenessReceipt(string Quote, int Points);
