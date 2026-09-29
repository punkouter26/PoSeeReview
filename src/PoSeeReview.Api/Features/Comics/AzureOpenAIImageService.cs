using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// gpt-image on the same Azure AI Foundry resource as the scorer, used when
/// <c>Ai:ImageProvider</c> is <c>AzureOpenAI</c>.
/// <para>
/// <b>Why it exists: price.</b> The image is nearly all of a comic's cost. gpt-image-1-mini at
/// <c>low</c> quality measured about 400 output image tokens for a 1024x1536 strip, roughly a
/// tenth of a Gemini image, and it needs no Google key: same endpoint and key as the chat
/// deployment.
/// </para>
/// <para>
/// Raw REST rather than <c>AzureOpenAIClient.GetImageClient</c>: the pinned SDK (2.1.0) predates
/// gpt-image and sends DALL-E parameters (<c>response_format</c>) the model rejects.
/// </para>
/// </summary>
public sealed class AzureOpenAIImageService : IImageGenerationService
{
    /// <summary>Telemetry label, and the provider key cost tracking is reported under.</summary>
    public const string ProviderLabel = "AzureOpenAI";

    private const string ApiVersion = "2025-04-01-preview";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AzureOpenAIOptions _options;
    private readonly ILogger<AzureOpenAIImageService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly AiCostTracker _costTracker;

    public AzureOpenAIImageService(
        IHttpClientFactory httpClientFactory,
        IOptions<AzureOpenAIOptions> options,
        ILogger<AzureOpenAIImageService> logger,
        TelemetryClient telemetryClient,
        AiCostTracker costTracker)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _telemetryClient = telemetryClient;
        _costTracker = costTracker;

        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint and AzureOpenAI:ApiKey must be configured when Ai:ImageProvider is AzureOpenAI.");
        }
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerateComicImageAsync(string narrative, IReadOnlyList<string> panelScenes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(narrative))
            throw new ArgumentException("Narrative cannot be empty", nameof(narrative));

        var panelCount = panelScenes.Count;
        if (panelCount is < 1 or > 4)
            throw new ArgumentException("Panel count must be between 1 and 4", nameof(panelScenes));

        var stopwatch = Stopwatch.StartNew();
        var (prompt, bluntedTerms) = ComicImagePrompt.Build(narrative, panelScenes);
        if (bluntedTerms.Count > 0)
        {
            _telemetryClient.GetMetric("AzureOpenAI.Image.PromptTermsBlunted").TrackValue(bluntedTerms.Count);
        }

        var body = new
        {
            prompt,
            n = 1,
            // The three sizes gpt-image accepts; each keeps the layout's panels landscape.
            size = panelCount switch { 1 or 3 => "1536x1024", 2 => "1024x1536", _ => "1024x1024" },
            quality = _options.ImageQuality,
            // JPEG only for the transfer: the overlay decodes and re-encodes it anyway.
            output_format = "jpeg"
        };

        var url = $"{_options.Endpoint.TrimEnd('/')}/openai/deployments/{_options.ImageDeployment}/images/generations?api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("api-key", _options.ApiKey);
        request.Content = JsonContent.Create(body);

        using var response = await _httpClientFactory.CreateClient("AzureOpenAIImageApi").SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A content-filter refusal is a 400 carrying a policy code. Like Gemini's SAFETY, it is
            // a signal about the source material and is surfaced, never retried with a stand-in.
            if (json.Contains("content_policy_violation", StringComparison.OrdinalIgnoreCase)
                || json.Contains("moderation_blocked", StringComparison.OrdinalIgnoreCase)
                || json.Contains("content_filter", StringComparison.OrdinalIgnoreCase))
            {
                _telemetryClient.GetMetric("AzureOpenAI.Image.Declined").TrackValue(1);
                throw new ImageDeclinedException(json);
            }

            _logger.LogError("Azure image API error {Status}: {Body}", (int)response.StatusCode, json);
            throw new InvalidOperationException($"Azure image API returned {(int)response.StatusCode}: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var b64 = root.GetProperty("data")[0].GetProperty("b64_json").GetString()
            ?? throw new InvalidOperationException("Azure image API returned no image data.");

        if (root.TryGetProperty("usage", out var usage))
        {
            _costTracker.Track(
                ProviderLabel,
                _options.ImageDeployment,
                usage.GetProperty("input_tokens").GetInt64(),
                usage.GetProperty("output_tokens").GetInt64());
        }

        stopwatch.Stop();
        _telemetryClient.GetMetric("AzureOpenAI.Image.DurationMs").TrackValue(stopwatch.Elapsed.TotalMilliseconds);
        _logger.LogInformation(
            "Generated Azure comic image ({PanelCount} panels, {Deployment}, {Quality}) in {Duration}ms",
            panelCount, _options.ImageDeployment, _options.ImageQuality, stopwatch.Elapsed.TotalMilliseconds);

        return Convert.FromBase64String(b64);
    }
}
