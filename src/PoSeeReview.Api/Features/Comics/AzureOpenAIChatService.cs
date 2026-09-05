using System.ClientModel;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    private readonly AzureOpenAIClient _openAIClient;
    private readonly string _deploymentName;
    private readonly ILogger<AzureOpenAIChatService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly AsyncRetryPolicy<ClientResult<ChatCompletion>> _chatRetryPolicy;

    public AzureOpenAIChatService(
        AzureOpenAIClient openAIClient,
        IConfiguration configuration,
        ILogger<AzureOpenAIChatService> logger,
        TelemetryClient telemetryClient)
    {
        _deploymentName = configuration["AzureOpenAI:DeploymentName"]
            ?? throw new InvalidOperationException("Azure OpenAI deployment name not configured");

        // Injected via DI so the underlying transport can be swapped for a mock in
        // test environments (AddMockedAiBoundaries) — no real Foundry calls / token spend.
        _openAIClient = openAIClient ?? throw new ArgumentNullException(nameof(openAIClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));

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

        var chatClient = _openAIClient.GetChatClient(_deploymentName);

        // Construct prompt for strangeness analysis
        var prompt = ChatPrompts.BuildAnalysisPrompt(validReviews);

        var chatMessages = new List<ChatMessage>
        {
            new SystemChatMessage(ChatPrompts.AnalysisSystemMessage),
            new UserChatMessage(prompt)
        };

        // NOTE: do NOT set MaxOutputTokenCount here. The 2.1.0 SDK serializes that property as
        // `max_tokens` in the wire format, but reasoning models (e.g. gpt-5.4-nano, 2026-03-17)
        // reject `max_tokens` with HTTP 400 "unsupported_parameter" and require
        // `max_completion_tokens` instead. The SDK doesn't yet auto-pick the right parameter,
        // so the safe move is to let the model use its default cap. If a future model needs
        // an explicit cap, set it via chatOptions.AdditionalProperties["max_completion_tokens"] = N.
        // 2026-06-15 incident: this parameter mismatch surfaced as 500 on every prod comic
        // generation after the model was rotated from gpt-4o-mini to gpt-5.4-nano.
        var chatOptions = new ChatCompletionOptions
        {
            Temperature = 0.3f, // Low temperature for consistent scoring
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

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
            _telemetryClient.GetMetric("AzureOpenAI.Chat.TotalTokens").TrackValue(usage.TotalTokenCount);
            _telemetryClient.GetMetric("AzureOpenAI.Chat.PromptTokens").TrackValue(usage.InputTokenCount);
            _telemetryClient.GetMetric("AzureOpenAI.Chat.CompletionTokens").TrackValue(usage.OutputTokenCount);

            // gpt-5.4-nano: ~$0.05 per 1K input tokens, ~$0.40 per 1K output tokens (GlobalStandard).
            // Use a blended $0.10/1K as a conservative cost-tracking estimate; the controller logs
            // prompt/completion tokens separately so a more accurate model can be plugged in later.
            var estimatedCost = usage.TotalTokenCount / 1000.0 * 0.10;
            _telemetryClient.GetMetric("AzureOpenAI.Chat.EstimatedCostUsd").TrackValue(estimatedCost);

            _logger.LogInformation(
                "Azure OpenAI usage - prompt: {PromptTokens}, completion: {CompletionTokens}, total: {TotalTokens}, estimated cost ${Cost:F4}",
                usage.InputTokenCount,
                usage.OutputTokenCount,
                usage.TotalTokenCount,
                estimatedCost);
        }

        return new StrangenessAnalysis(score, panelCount, result.Narrative);
    }

    /// <summary>
    /// Generates concise per-panel captions from a comic narrative using GPT.
    /// Uses low token budget to keep cost minimal.
    /// </summary>
    public async Task<List<string>> GeneratePanelDialogueAsync(string narrative, int panelCount, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(narrative))
            return ChatPrompts.FallbackDialogue(narrative ?? string.Empty, panelCount);

        var chatClient = _openAIClient.GetChatClient(_deploymentName);

        var prompt = ChatPrompts.BuildCaptionPrompt(narrative, panelCount);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(ChatPrompts.CaptionSystemMessage),
            new UserChatMessage(prompt)
        };

        // Same reasoning as AnalyzeStrangenessAsync: don't set MaxOutputTokenCount. See the
        // long comment there for why reasoning models (gpt-5.4-nano) reject `max_tokens`.
        var options = new ChatCompletionOptions
        {
            Temperature = 0.6f,
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        try
        {
            var response = await _chatRetryPolicy.ExecuteAsync(
                ct => chatClient.CompleteChatAsync(messages, options, ct),
                cancellationToken);

            _telemetryClient.GetMetric("AzureOpenAI.Chat.Requests").TrackValue(1);

            var json = response.Value.Content[0].Text;
            var result = JsonSerializer.Deserialize<PanelCaptionsResult>(json);

            if (result?.Captions is { Count: > 0 } captions)
            {
                _logger.LogInformation("Generated {Count} panel captions via GPT", captions.Count);
                return captions;
            }

            _logger.LogWarning("GPT returned empty captions, using sentence fallback");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate panel captions via GPT, using sentence fallback");
        }

        return ChatPrompts.FallbackDialogue(narrative, panelCount);
    }

}
