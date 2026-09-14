using System.ClientModel;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Contracts;
using Polly.Retry;
using Polly;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Azure AI Foundry service for analyzing review strangeness using the
/// <c>gpt-5.4-nano</c> deployment in <c>po-aiservices-shared</c>
/// (verified 2026-06-14 as the sole deployment in the resource).
/// Uses the Azure.AI.OpenAI SDK to connect to Azure AI Foundry (Cognitive Services).
/// Returns strangeness score (0-100) and narrative paragraph for comic generation.
/// </summary>
public class AzureOpenAIChatService : IChatCompletionService
{
    /// <summary>Telemetry label for this provider, and the key <c>AiPricing:Models</c> is read with.</summary>
    public const string ProviderLabel = "AzureOpenAI";

    private readonly AzureOpenAIClient _openAIClient;
    private readonly AzureOpenAIOptions _options;
    private readonly ILogger<AzureOpenAIChatService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly AiCostTracker _costTracker;
    private readonly AsyncRetryPolicy<ClientResult<ChatCompletion>> _chatRetryPolicy;

    public AzureOpenAIChatService(
        AzureOpenAIClient openAIClient,
        IOptions<AzureOpenAIOptions> options,
        ILogger<AzureOpenAIChatService> logger,
        TelemetryClient telemetryClient,
        AiCostTracker costTracker)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(_options.DeploymentName))
        {
            throw new InvalidOperationException("AzureOpenAI deployment name not configured");
        }

        // Injected via DI so the underlying transport can be swapped for a mock in
        // test environments (AddMockedAiBoundaries) — no real Foundry calls / token spend.
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        _costTracker = costTracker ?? throw new ArgumentNullException(nameof(costTracker));

        _chatRetryPolicy = Policy<ClientResult<ChatCompletion>>
            .Handle<RequestFailedException>(AzureRetryUtils.IsTransientFailure)
            .Or<HttpRequestException>()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), (outcome, timespan, attempt, _) =>
            {
                var reason = outcome.Exception?.Message ?? outcome.Result?.GetRawResponse()?.Status.ToString() ?? "unknown";
                _logger.LogWarning(
                    "Retrying Azure OpenAI chat completion due to {Reason}. Attempt {Attempt}. Waiting {Delay} seconds",
                    reason,
                    attempt,
                    timespan.TotalSeconds);
            });
    }

    /// <summary>
    /// Analyzes restaurant reviews for strangeness and generates a narrative paragraph.
    /// </summary>
    /// <param name="reviews">List of review texts (5-10 reviews recommended)</param>
    /// <param name="cancellationToken">Cancels the (potentially slow) model call when the caller abandons the request</param>
    /// <returns>Score, panel count, and the narrative the image model draws from.</returns>
    /// <exception cref="ArgumentException">If reviews list is null or empty</exception>
    public async Task<StrangenessAnalysis> AnalyzeStrangenessAsync(List<string> reviews, CancellationToken cancellationToken = default)
    {
        if (reviews == null || reviews.Count == 0)
            throw new ArgumentException("Reviews list cannot be empty", nameof(reviews));

        // Filter empty reviews
        var validReviews = reviews.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (validReviews.Count == 0)
            throw new ArgumentException("No valid reviews provided", nameof(reviews));

        var chatClient = _openAIClient.GetChatClient(_options.DeploymentName);

        // Construct prompt for strangeness analysis
        var prompt = ChatPrompts.BuildAnalysisPrompt(validReviews);

        var chatMessages = new List<ChatMessage>
        {
            new SystemChatMessage(ChatPrompts.AnalysisSystemMessage),
            new UserChatMessage(prompt)
        };

        // Temperature 0.3 for a repeatable score. The completion cap is null unless configured,
        // and it only reaches the wire on an instruct deployment — on a reasoning one the SDK
        // cannot express it and its reasoning tokens would eat it anyway. See ChatTokenBudget
        // for the full reasoning, and StartupSecretValidator for the warning raised when a cap is
        // configured against a model family that will ignore it.
        var chatOptions = ChatTokenBudget.Build(
            temperature: 0.3f,
            maxCompletionTokens: _options.MaxCompletionTokens,
            isReasoningModel: _options.IsReasoningModel);

        // Propagate the caller's token: this call can take 40+ seconds on reasoning models, and
        // an abandoned browser request should stop the pipeline instead of completing a paid call.
        var response = await _chatRetryPolicy.ExecuteAsync(
            ct => chatClient.CompleteChatAsync(chatMessages, chatOptions, ct),
            cancellationToken);

        _telemetryClient.GetMetric("AzureOpenAI.Chat.Requests").TrackValue(1);

        // Parse JSON response
        var jsonResponse = response.Value.Content[0].Text;
        var result = JsonSerializer.Deserialize<StrangenessAnalysisResult>(jsonResponse)
            ?? throw new InvalidOperationException("Failed to parse OpenAI response");

        // Clamp score to 0-100 range and panel count to 1-2
        var score = Math.Clamp(result.StrangenessScore, 0, 100);
        var panelCount = Math.Clamp(result.PanelCount, 1, 2);

        if (response.Value.Usage is { } usage)
        {
            // The rate comes from AiPricing, not from a constant here. The previous estimate was
            // a single blended $0.10/1K over total tokens, with the input rate 8x cheaper than
            // the output rate and a comment conceding the figure was a guess — which meant the
            // number moved with the prompt/completion mix rather than with the price, and so
            // could not answer the only question a cost metric is asked.
            _costTracker.Track(ProviderLabel, _options.DeploymentName, usage.InputTokenCount, usage.OutputTokenCount);
        }

        return new StrangenessAnalysis(
            score,
            panelCount,
            result.Narrative,
            ChatPrompts.NormalizeCaptions(result.Captions, result.Narrative, panelCount));
    }
}
