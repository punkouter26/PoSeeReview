using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Options;

namespace PoSeeReview.Api.Features.Comics;

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

    /// <summary>Tokens billed, dimensioned by <c>Provider</c> and <c>Direction</c> (input/output).</summary>
    public const string TokensMetricName = "Ai.Tokens";

    public void Track(string provider, string model, long inputTokens, long outputTokens)
    {
        var pricing = options.Value;

        // A local runtime has no marginal token price. Reporting one would put a made-up dollar
        // figure into the same series as real spend and make the series unusable.
        var isFree = pricing.FreeProviders.Contains(provider);
        var rate = isFree ? null : pricing.Resolve(model);

        var cost = isFree
            ? 0
            : inputTokens / 1000.0 * rate!.InputPer1KTokens + outputTokens / 1000.0 * rate!.OutputPer1KTokens;

        telemetryClient.GetMetric(CostMetricName, "Provider", "Model").TrackValue(cost, provider, model);
        telemetryClient.GetMetric(TokensMetricName, "Provider", "Direction").TrackValue(inputTokens, provider, "input");
        telemetryClient.GetMetric(TokensMetricName, "Provider", "Direction").TrackValue(outputTokens, provider, "output");
    }
}
