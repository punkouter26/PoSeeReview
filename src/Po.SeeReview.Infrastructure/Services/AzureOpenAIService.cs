using System.ClientModel;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Po.SeeReview.Core.Interfaces;
using Polly;
using Polly.Retry;

namespace Po.SeeReview.Infrastructure.Services;

/// <summary>
/// Azure OpenAI service for analyzing review strangeness using GPT-4o-mini.
/// Returns strangeness score (0-100) and narrative paragraph for comic generation.
/// </summary>
public class AzureOpenAIService : IAzureOpenAIService
{
    private readonly AzureOpenAIClient _openAIClient;
    private readonly string _deploymentName;
    private readonly ILogger<AzureOpenAIService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly AsyncRetryPolicy<ClientResult<ChatCompletion>> _chatRetryPolicy;

    public AzureOpenAIService(
        IConfiguration configuration,
        ILogger<AzureOpenAIService> logger,
        TelemetryClient telemetryClient)
    {
        var endpoint = configuration["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("Azure OpenAI endpoint not configured");

        var apiKey = configuration["AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException("Azure OpenAI API key not configured");

        _deploymentName = configuration["AzureOpenAI:DeploymentName"]
            ?? throw new InvalidOperationException("Azure OpenAI deployment name not configured");

        _openAIClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));

        _chatRetryPolicy = Policy<ClientResult<ChatCompletion>>
            .Handle<RequestFailedException>(IsTransientFailure)
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
    /// <returns>Tuple of (StrangenessScore 0-100, PanelCount 1-4, Narrative paragraph)</returns>
    /// <exception cref="ArgumentException">If reviews list is null or empty</exception>
    public async Task<(int StrangenessScore, int PanelCount, string Narrative)> AnalyzeStrangenessAsync(List<string> reviews)
    {
        if (reviews == null || reviews.Count == 0)
            throw new ArgumentException("Reviews list cannot be empty", nameof(reviews));

        // Filter empty reviews
        var validReviews = reviews.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (validReviews.Count == 0)
            throw new ArgumentException("No valid reviews provided", nameof(reviews));

        var chatClient = _openAIClient.GetChatClient(_deploymentName);

        // Construct prompt for strangeness analysis
        var prompt = BuildAnalysisPrompt(validReviews);

        var chatMessages = new List<ChatMessage>
        {
            new SystemChatMessage("You are an expert at analyzing restaurant reviews for unusual, strange, or surreal elements. You return JSON responses only."),
            new UserChatMessage(prompt)
        };

        var chatOptions = new ChatCompletionOptions
        {
            Temperature = 0.3f, // Low temperature for consistent scoring
            MaxOutputTokenCount = 200, // Reduced from 500 to minimize costs while still allowing narrative
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        var response = await _chatRetryPolicy.ExecuteAsync(() => chatClient.CompleteChatAsync(chatMessages, chatOptions));

        _telemetryClient.GetMetric("AzureOpenAI.Chat.Requests").TrackValue(1);

        // Parse JSON response
        var jsonResponse = response.Value.Content[0].Text;
        var result = JsonSerializer.Deserialize<StrangenessAnalysisResult>(jsonResponse)
            ?? throw new InvalidOperationException("Failed to parse OpenAI response");

        // Clamp score to 0-100 range and panel count to 1-4
        var score = Math.Clamp(result.StrangenessScore, 0, 100);
        var panelCount = Math.Clamp(result.PanelCount, 1, 4);

        if (response.Value.Usage is { } usage)
        {
            _telemetryClient.GetMetric("AzureOpenAI.Chat.TotalTokens").TrackValue(usage.TotalTokenCount);
            _telemetryClient.GetMetric("AzureOpenAI.Chat.PromptTokens").TrackValue(usage.InputTokenCount);
            _telemetryClient.GetMetric("AzureOpenAI.Chat.CompletionTokens").TrackValue(usage.OutputTokenCount);

            var estimatedCost = usage.TotalTokenCount / 1000.0 * 0.15; // GPT-4o-mini pricing ($0.15 per 1K tokens)
            _telemetryClient.GetMetric("AzureOpenAI.Chat.EstimatedCostUsd").TrackValue(estimatedCost);

            _logger.LogInformation(
                "Azure OpenAI usage - prompt: {PromptTokens}, completion: {CompletionTokens}, total: {TotalTokens}, estimated cost ${Cost:F4}",
                usage.InputTokenCount,
                usage.OutputTokenCount,
                usage.TotalTokenCount,
                estimatedCost);
        }

        return (score, panelCount, result.Narrative);
    }

    private static string BuildAnalysisPrompt(List<string> reviews)
    {
        var reviewsText = string.Join("\n\n", reviews.Select((r, i) => $"Review {i + 1}: {r}"));

        return $@"Analyze these restaurant reviews for strangeness. Rate the overall strangeness on a scale of 0-100:
- 0-20: Completely normal, typical restaurant experience
- 21-40: Slightly unusual details or phrasing
- 41-60: Moderately strange situations or observations
- 61-80: Very weird, surreal, or unexpected experiences
- 81-100: Extremely bizarre, dreamlike, or nonsensical content

Also write a concise narrative paragraph (1-3 sentences) summarizing the strangest aspects for comic generation.
Determine the optimal number of panels (1-4) for the comic based on narrative complexity:
- 1 panel: Single moment, simple observation, or quick joke
- 2 panels: Before/after, cause/effect, or simple contrast
- 3 panels: Setup, escalation, punchline
- 4 panels: Full story arc with setup, development, climax, resolution

Reviews:
{reviewsText}

Return JSON in this exact format:
{{
  ""strangenessScore"": 75,
  ""panelCount"": 3,
  ""narrative"": ""A concise summary of the strangest elements suitable for a comic strip.""
}}";
    }

    private sealed class StrangenessAnalysisResult
    {
        [JsonPropertyName("strangenessScore")]
        public int StrangenessScore { get; set; }

        [JsonPropertyName("panelCount")]
        public int PanelCount { get; set; } = 4; // Default to 4 panels if not specified

        [JsonPropertyName("narrative")]
        public string Narrative { get; set; } = string.Empty;
    }

    private static bool IsTransientFailure(RequestFailedException exception)
    {
        return exception.Status is 408 or 429 or >= 500;
    }
}
