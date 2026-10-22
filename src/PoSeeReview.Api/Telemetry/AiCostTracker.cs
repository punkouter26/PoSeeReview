using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Options;

namespace PoSeeReview.Api.Telemetry;

/// <summary>
/// The single place a model call reports what it cost, tagged by provider and model.
/// <para>
/// Every chat implementation used to compute its own estimate against its own hardcoded rate
/// and emit it under its own metric name (<c>AzureOpenAI.Chat.EstimatedCostUsd</c>), which meant
/// there was no metric whose value was "what this app spent" — only several metrics that each
/// answered a slightly different question, on scales that could not be compared.
/// </para>
/// <para>
/// Dimensions rather than metric names: one <see cref="CostMetricName"/> with
/// <c>Provider</c>/<c>Model</c> dimensions can be summed for a total and split for an
/// attribution, and adding a provider does not add a metric.
/// </para>
/// </summary>
public sealed class AiCostTracker(
    IOptions<AiPricingOptions> options,
    TelemetryClient telemetryClient)
{
    /// <summary>Total USD spent, dimensioned by <c>Provider</c> and <c>Model</c>.</summary>
    public const string CostMetricName = "Ai.Cost.Usd";

    /// <summary>Tokens billed, dimensioned by <c>Provider</c> and <c>Direction</c> (input/cached/output).</summary>
    public const string TokensMetricName = "Ai.Tokens";

    /// <summary>
    /// Records one call. <paramref name="cachedInputTokens"/> is the part of
    /// <paramref name="inputTokens"/> served from the provider's prompt cache: it bills at the
    /// cached rate, and the <c>cached</c> series is how to see whether the cache is hitting at all.
    /// </summary>
    public void Track(string provider, string model, long inputTokens, long outputTokens, long cachedInputTokens = 0)
    {
        var pricing = options.Value;

        // A local runtime has no marginal token price. Reporting one would put a made-up dollar
        // figure into the same series as real spend and make the series unusable.
        var isFree = pricing.FreeProviders.Contains(provider);
        var rate = isFree ? null : pricing.Resolve(model);

        var cached = Math.Clamp(cachedInputTokens, 0, inputTokens);
        var cost = isFree ? 0 : PriceOf(rate!, inputTokens, outputTokens, cached);

        telemetryClient.GetMetric(CostMetricName, "Provider", "Model").TrackValue(cost, provider, model);
        telemetryClient.GetMetric(TokensMetricName, "Provider", "Direction").TrackValue(inputTokens, provider, "input");
        telemetryClient.GetMetric(TokensMetricName, "Provider", "Direction").TrackValue(cached, provider, "cached");
        telemetryClient.GetMetric(TokensMetricName, "Provider", "Direction").TrackValue(outputTokens, provider, "output");
    }

    /// <summary>USD for one call; <paramref name="cachedInputTokens"/> is a subset of <paramref name="inputTokens"/>.</summary>
    internal static double PriceOf(AiModelRate rate, long inputTokens, long outputTokens, long cachedInputTokens) =>
        (inputTokens - cachedInputTokens) / 1000.0 * rate.InputPer1KTokens
        + cachedInputTokens / 1000.0 * (rate.CachedInputPer1KTokens ?? rate.InputPer1KTokens)
        + outputTokens / 1000.0 * rate.OutputPer1KTokens;
}
