namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Published token prices, so cost telemetry reports a number that can be trusted.
/// <para>
/// Before this, the only cost signal in the app was a single hardcoded blended rate
/// (<c>$0.10 / 1K</c>) applied to total tokens, with a comment admitting the input and output
/// rates differ by 8x and that the figure was a guess. A guessed blended rate cannot answer the
/// only question it exists to answer — "did that change make this cheaper?" — because it moves
/// with the input/output mix rather than with the price.
/// </para>
/// <para>
/// Rates are configuration, not code: providers reprice, and a reprice must not need a deploy.
/// Everything here is per 1000 tokens in USD.
/// </para>
/// </summary>
public class AiPricingOptions
{
    public const string SectionName = "AiPricing";

    /// <summary>
    /// Blended rate applied when the model has no entry in <see cref="Models"/>. Deliberately
    /// pessimistic: an unknown model should over-report rather than under-report, because the
    /// number is a spend alarm and a quietly low one is worse than a noisy high one.
    /// </summary>
    public double FallbackPer1KTokens { get; set; } = 0.10;

    /// <summary>
    /// Providers with no marginal token price, keyed case-insensitively by the provider label
    /// the tracker is called with. Ollama is here because a model running on your own machine
    /// costs electricity, not tokens, and reporting a dollar figure for it would put a fictional
    /// cost into the same chart as a real one.
    /// </summary>
    public HashSet<string> FreeProviders { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "Ollama" };

    /// <summary>Per-model rates, keyed by model or deployment id.</summary>
    public Dictionary<string, AiModelRate> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Looks up the rate for a model, falling back to a single blended
    /// <see cref="FallbackPer1KTokens"/> pair when the model is unknown.
    /// </summary>
    public AiModelRate Resolve(string model)
    {
        if (!string.IsNullOrWhiteSpace(model) && Models.TryGetValue(model, out var rate))
        {
            return rate;
        }

        return new AiModelRate
        {
            InputPer1KTokens = FallbackPer1KTokens,
            OutputPer1KTokens = FallbackPer1KTokens
        };
    }
}

/// <summary>Price of one model, per 1000 tokens in USD.</summary>
public class AiModelRate
{
    public double InputPer1KTokens { get; set; }

    public double OutputPer1KTokens { get; set; }
}
