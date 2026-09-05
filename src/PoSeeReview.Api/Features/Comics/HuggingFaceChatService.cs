using System.ClientModel;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// HuggingFace chat backend for strangeness analysis + panel captions, used in place of
/// <see cref="AzureOpenAIChatService"/> when <c>Ai:ImageProvider</c> is <c>HuggingFace</c>. HF's chat router is
/// OpenAI-wire-compatible, so this reuses the OpenAI SDK's <see cref="ChatClient"/> pointed at
/// <c>router.huggingface.co/v1</c>. The prompts mirror <see cref="AzureOpenAIChatService"/> so both
/// providers produce the same JSON contract; open models are a bit looser about JSON, so parsing
/// here also tolerates markdown code fences.
/// </summary>
public sealed class HuggingFaceChatService : IChatCompletionService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<HuggingFaceChatService> _logger;
    private readonly TelemetryClient _telemetryClient;

    public HuggingFaceChatService(
        IOptions<HuggingFaceOptions> options,
        ILogger<HuggingFaceChatService> logger,
        TelemetryClient telemetryClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));

        var opts = options.Value ?? throw new ArgumentNullException(nameof(options));
        var apiKey = HuggingFaceTokenResolver.Resolve(opts.ApiKey)
            ?? throw new InvalidOperationException(
                "No HuggingFace token found. Set 'HuggingFace:ApiKey' (Key Vault as " +
                "'PoSeeReview--HuggingFace--ApiKey', or user-secrets), set the HF_TOKEN env var, " +
                "or run 'hf auth login'. The token needs the 'Inference Providers' permission.");

        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(opts.ChatBaseUrl) });
        _chatClient = client.GetChatClient(opts.ChatModel);

        _logger.LogInformation("HuggingFaceChatService initialised. Chat model: {Model}", opts.ChatModel);
    }

    /// <inheritdoc />
    public async Task<StrangenessAnalysis> AnalyzeStrangenessAsync(
        List<string> reviews, CancellationToken cancellationToken = default)
    {
        if (reviews == null || reviews.Count == 0)
            throw new ArgumentException("Reviews list cannot be empty", nameof(reviews));

        var validReviews = reviews.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (validReviews.Count == 0)
            throw new ArgumentException("No valid reviews provided", nameof(reviews));

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(ChatPrompts.AnalysisSystemMessage),
            new UserChatMessage(ChatPrompts.BuildAnalysisPrompt(validReviews))
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.3f,
            MaxOutputTokenCount = 800,
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
        _telemetryClient.GetMetric("HuggingFace.Chat.Requests").TrackValue(1);
        TrackUsage(response.Value);

        var result = DeserializeLenient<StrangenessAnalysisResult>(response.Value.Content[0].Text)
            ?? throw new InvalidOperationException("Failed to parse HuggingFace chat response");

        var score = Math.Clamp(result.StrangenessScore, 0, 100);
        var panelCount = Math.Clamp(result.PanelCount, 1, 2);
        return new StrangenessAnalysis(score, panelCount, result.Narrative);
    }

    /// <inheritdoc />
    public async Task<List<string>> GeneratePanelDialogueAsync(
        string narrative, int panelCount, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(narrative))
            return ChatPrompts.FallbackDialogue(narrative ?? string.Empty, panelCount);

        var prompt = ChatPrompts.BuildCaptionPrompt(narrative, panelCount);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(ChatPrompts.CaptionSystemMessage),
            new UserChatMessage(prompt)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.6f,
            MaxOutputTokenCount = 300,
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        try
        {
            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
            _telemetryClient.GetMetric("HuggingFace.Chat.Requests").TrackValue(1);
            TrackUsage(response.Value);

            var result = DeserializeLenient<PanelCaptionsResult>(response.Value.Content[0].Text);
            if (result?.Captions is { Count: > 0 } captions)
            {
                _logger.LogInformation("Generated {Count} panel captions via HuggingFace", captions.Count);
                return captions;
            }

            _logger.LogWarning("HuggingFace returned empty captions, using sentence fallback");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate panel captions via HuggingFace, using sentence fallback");
        }

        return ChatPrompts.FallbackDialogue(narrative, panelCount);
    }

    private void TrackUsage(ChatCompletion completion)
    {
        if (completion.Usage is { } usage)
        {
            _telemetryClient.GetMetric("HuggingFace.Chat.TotalTokens").TrackValue(usage.TotalTokenCount);
            _telemetryClient.GetMetric("HuggingFace.Chat.PromptTokens").TrackValue(usage.InputTokenCount);
            _telemetryClient.GetMetric("HuggingFace.Chat.CompletionTokens").TrackValue(usage.OutputTokenCount);
        }
    }

    /// <summary>
    /// Tolerant JSON parse: open models sometimes wrap JSON in ```json fences despite the
    /// response_format hint. Strip the fence and parse the first {...} / [...] block.
    /// </summary>
    private static T? DeserializeLenient<T>(string content) where T : class
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var text = content.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
                text = text[(firstNewline + 1)..];
            text = text.TrimEnd('`', '\n', '\r', ' ').TrimEnd('`');
        }

        var start = text.IndexOfAny(['{', '[']);
        var end = text.LastIndexOfAny(['}', ']']);
        if (start >= 0 && end > start)
            text = text[start..(end + 1)];

        return JsonSerializer.Deserialize<T>(text);
    }

}
