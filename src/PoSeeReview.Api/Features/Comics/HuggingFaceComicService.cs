using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// HuggingFace Inference Providers image generation (FLUX by default), used in place of
/// <see cref="GeminiComicService"/> when <c>UseHuggingFace</c> is on.
///
/// The key advantage over Imagen: FLUX accepts a <c>negative_prompt</c>. Imagen has no
/// negative channel and bakes garbled pseudo-English into speech bubbles and narration boxes
/// that the caption overlay can't fully cover. Here we push all lettering concepts into the
/// negative prompt and keep the positive prompt purely about wordless, pantomime action — so
/// the art comes out clean and <see cref="ComicTextOverlayService"/> supplies the real English.
/// </summary>
public sealed class HuggingFaceComicService : IImageGenerationService
{
    // Everything that must NOT appear in the art. This is the whole point of using FLUX:
    // it reliably suppresses the baked-in gibberish text that broke the Imagen output.
    private const string NegativePrompt =
        "text, words, letters, captions, speech bubbles, word balloons, dialogue balloons, " +
        "narration boxes, thought bubbles, signs, labels, menus with text, writing, typography, " +
        "handwriting, gibberish text, watermark, signature, logo, subtitles, " +
        "blurry, low quality, deformed, extra limbs, disfigured";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HuggingFaceComicService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly HuggingFaceOptions _options;
    private readonly string _apiKey;

    public HuggingFaceComicService(
        IHttpClientFactory httpClientFactory,
        IOptions<HuggingFaceOptions> options,
        ILogger<HuggingFaceComicService> logger,
        TelemetryClient telemetryClient)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));

        _apiKey = HuggingFaceTokenResolver.Resolve(_options.ApiKey)
            ?? throw new InvalidOperationException(
                "No HuggingFace token found. Set 'HuggingFace:ApiKey' (Key Vault as " +
                "'PoSeeReview--HuggingFace--ApiKey', or user-secrets), set the HF_TOKEN env var, " +
                "or run 'hf auth login'. The token needs the 'Inference Providers' permission.");

        _logger.LogInformation("HuggingFaceComicService initialised. Image model: {Model}", _options.ImageModel);
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerateComicImageAsync(string narrative, int panelCount, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(narrative))
            throw new ArgumentException("Narrative cannot be empty", nameof(narrative));

        if (panelCount is < 1 or > 4)
            throw new ArgumentException("Panel count must be between 1 and 4", nameof(panelCount));

        var stopwatch = Stopwatch.StartNew();
        var prompt = BuildComicPrompt(narrative, panelCount);

        var body = new
        {
            inputs = prompt,
            parameters = new
            {
                negative_prompt = NegativePrompt,
                num_inference_steps = _options.ImageSteps,
                width = 1024,
                height = 1024
            }
        };

        var client = _httpClientFactory.CreateClient("HuggingFaceApi");
        var url = $"{_options.ImageBaseUrl.TrimEnd('/')}/{_options.ImageModel}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = JsonContent.Create(body);

        _logger.LogDebug("Calling HuggingFace text-to-image: {Url}", url);

        // Transient failures (429/503 model-warming/timeouts) are handled by the standard
        // resilience handler on the "HuggingFaceApi" client — no hand-rolled retry here.
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("HuggingFace image API error {Status}: {Body}", (int)response.StatusCode, errorBody);
            throw new InvalidOperationException(
                $"HuggingFace image API returned {(int)response.StatusCode}: {errorBody}");
        }

        var imageBytes = await ReadImageBytesAsync(response, cancellationToken);

        stopwatch.Stop();
        _telemetryClient.GetMetric("HuggingFace.Image.Requests").TrackValue(1);
        _telemetryClient.GetMetric("HuggingFace.Image.DurationMs").TrackValue(stopwatch.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Generated HuggingFace comic image ({PanelCount} panels, {Model}) in {Duration}ms, {Size} bytes",
            panelCount, _options.ImageModel, stopwatch.Elapsed.TotalMilliseconds, imageBytes.Length);

        return imageBytes;
    }

    /// <summary>
    /// The hf-inference provider returns raw image bytes; some routed providers instead return
    /// JSON carrying a base64 image. Handle both so a provider swap in config doesn't break us.
    /// </summary>
    private static async Task<byte[]> ReadImageBytesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);

        // JSON path: look for a base64 image field ({"image":"..."} or [{"b64_json":"..."}]).
        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        var root = json.RootElement;
        var element = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
            ? root[0]
            : root;

        foreach (var field in new[] { "b64_json", "image", "images" })
        {
            if (element.TryGetProperty(field, out var value))
            {
                var b64 = value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0
                    ? value[0].GetString()
                    : value.GetString();
                if (!string.IsNullOrWhiteSpace(b64))
                    return Convert.FromBase64String(b64);
            }
        }

        throw new InvalidOperationException(
            $"HuggingFace image response was '{mediaType}' with no recognizable image payload: {root.GetRawText()}");
    }

    /// <summary>
    /// Positive prompt describes ONLY wordless, pantomime action — no mention of text, bubbles,
    /// or "no text" (naming a concept, even to forbid it, nudges diffusion models to draw it).
    /// All lettering suppression lives in <see cref="NegativePrompt"/>.
    /// </summary>
    private static string BuildComicPrompt(string narrative, int panelCount)
    {
        var panelLayout = panelCount switch
        {
            1 => "a single wide comic panel filling the frame",
            2 => "a two-panel comic strip, equal panels stacked vertically",
            3 => "a three-panel comic strip flowing left to right",
            _ => "a four-panel comic strip in a 2x2 grid, read left to right, top to bottom"
        };

        return $"""
Create {panelLayout} in a clean, modern cartoon illustration style — vibrant colors, bold outlines,
exaggerated facial expressions and body language. Wordless, silent, pantomime storytelling in the
tradition of silent-film slapstick: every emotion carried purely by faces, gestures, and posture.

Depict this scene through action alone:
"{narrative}"

Consistent characters across panels with matching outfits. Clean black panel gutters separating
exactly {panelCount} panel(s). Every wall, sign, menu, and surface is a plain solid color or simple
decoration only. Empty, blank surfaces throughout.
""";
    }
}
