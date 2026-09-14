using System.ClientModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// HuggingFace chat backend for strangeness analysis, used when <c>Ai:ChatProvider</c> is
/// <c>HuggingFace</c>. HF's chat router is OpenAI-wire-compatible, so this reuses the OpenAI
/// SDK's <see cref="ChatClient"/> pointed at <c>router.huggingface.co/v1</c>.
/// <para>
/// The call itself lives in <see cref="OpenAiWireChat"/> rather than here, because Ollama needs
/// exactly the same one — same messages, same lenient parse, same clamping — and it is the same
/// argument <see cref="ChatPrompts"/> already makes about the prompt text. What is genuinely
/// HF-specific is the token, the base URL and the pricing label.
/// </para>
/// </summary>
public sealed class HuggingFaceChatService : IChatCompletionService
{
    /// <summary>Telemetry label for this provider, and the key <c>AiPricing:Models</c> is read with.</summary>
    public const string ProviderLabel = "HuggingFace";

    private readonly ChatClient _chatClient;
    private readonly HuggingFaceOptions _options;
    private readonly ILogger<HuggingFaceChatService> _logger;
    private readonly AiCostTracker _costTracker;

    public HuggingFaceChatService(
        IOptions<HuggingFaceOptions> options,
        ILogger<HuggingFaceChatService> logger,
        AiCostTracker costTracker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _costTracker = costTracker ?? throw new ArgumentNullException(nameof(costTracker));

        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        var apiKey = HuggingFaceTokenResolver.Resolve(_options.ApiKey)
            ?? throw new InvalidOperationException(
                "No HuggingFace token found. Set 'HuggingFace:ApiKey' (Key Vault as " +
                "'PoSeeReview--HuggingFace--ApiKey', or user-secrets), set the HF_TOKEN env var, " +
                "or run 'hf auth login'. The token needs the 'Inference Providers' permission.");

        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(_options.ChatBaseUrl) });
        _chatClient = client.GetChatClient(_options.ChatModel);

        _logger.LogInformation("HuggingFaceChatService initialised. Chat model: {Model}", _options.ChatModel);
    }

    /// <inheritdoc />
    public async Task<StrangenessAnalysis> AnalyzeStrangenessAsync(
        List<string> reviews, CancellationToken cancellationToken = default)
    {
        if (reviews == null || reviews.Count == 0)
        {
            throw new ArgumentException("Reviews list cannot be empty", nameof(reviews));
        }

        var result = await OpenAiWireChat.AnalyzeAsync(
            _chatClient,
            reviews,
            temperature: 0.3f,
            maxCompletionTokens: _options.AnalysisMaxTokens,
            // An open instruct model through the router has no hidden reasoning budget sharing
            // the allowance, so the cap is safe here and the typed wire property is correct.
            isReasoningModel: false,
            cancellationToken);

        _costTracker.Track(ProviderLabel, _options.ChatModel, result.InputTokens, result.OutputTokens);

        return result.Analysis;
    }

    /// <inheritdoc />
    public async Task<PoSeeReview.Shared.Dtos.ComicAudioSkit> GenerateSkitAsync(
        string restaurantName,
        string narrative,
        IReadOnlyList<string>? captions,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(narrative))
        {
            throw new ArgumentException("Narrative cannot be empty", nameof(narrative));
        }

        var result = await OpenAiWireChat.GenerateSkitAsync(
            _chatClient,
            restaurantName,
            narrative,
            captions,
            temperature: 0.7f,
            maxCompletionTokens: _options.AnalysisMaxTokens,
            // Same as the analysis path: the HF router exposes an instruct model here.
            isReasoningModel: false,
            cancellationToken);

        _costTracker.Track(ProviderLabel, _options.ChatModel, result.InputTokens, result.OutputTokens);
        return result.Skit;
    }
}
