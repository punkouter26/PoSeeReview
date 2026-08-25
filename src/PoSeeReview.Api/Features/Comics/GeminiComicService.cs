using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Google Gemini image generation service (generateContent image models).
/// Uses the Generative Language REST API.
/// Requires <c>Google:GeminiApiKey</c> in configuration (stored as "PoSeeReview--Google--GeminiApiKey" in Key Vault).
/// Model must expose <c>generateContent</c>; the Imagen <c>predict</c> family is not available on this key.
/// </summary>
public sealed class GeminiComicService : IImageGenerationService
{
    // Verified against ListModels for this project's key on 2026-08-25: NO model exposes the
    // Imagen ":predict" method any more, which is why every generation was failing with
    //   404 "models/imagen-4.0-fast-generate-001 is not found for API version v1beta,
    //        or is not supported for predict"
    // The six image-capable models all expose ":generateContent" instead, returning the image as
    // inline base64 in a candidate part. Overridable via Google:GeminiModel — but any replacement
    // must also be a generateContent image model, not an Imagen predict model.
    private const string DefaultModel = "gemini-2.5-flash-image";
    private const string ApiBase = "https://generativelanguage.googleapis.com/v1beta/models";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GeminiComicService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiComicService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GeminiComicService> logger,
        TelemetryClient telemetryClient)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));

        _apiKey = configuration["Google:GeminiApiKey"]
            ?? throw new InvalidOperationException(
                "Google:GeminiApiKey is not configured. " +
                "Add 'PoSeeReview--Google--GeminiApiKey' to Key Vault.");

        _model = configuration["Google:GeminiModel"] ?? DefaultModel;

        _logger.LogInformation("GeminiComicService initialised. Model: {Model} (generateContent image API)", _model);
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerateComicImageAsync(string narrative, int panelCount, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(narrative))
            throw new ArgumentException("Narrative cannot be empty", nameof(narrative));

        if (panelCount is < 1 or > 4)
            throw new ArgumentException("Panel count must be between 1 and 4", nameof(panelCount));

        var stopwatch = Stopwatch.StartNew();
        var prompt = BuildComicPrompt(SanitizeNarrative(narrative), panelCount);

        // Transient failures (429/503/timeouts) are handled by the standard resilience handler
        // configured on the "GeminiApi" HttpClient, so no hand-rolled retry is needed here.
        byte[] imageBytes;
        try
        {
            imageBytes = await GenerateAsync(prompt, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("safety", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("declined", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("blocked", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Gemini blocked content, falling back to generic comic prompt");
            imageBytes = await GenerateAsync(BuildFallbackComicPrompt(panelCount), cancellationToken);
        }

        stopwatch.Stop();

        _telemetryClient.GetMetric("Gemini.Image.Requests").TrackValue(1);
        _telemetryClient.GetMetric("Gemini.Image.DurationMs").TrackValue(stopwatch.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Generated Gemini comic image ({PanelCount} panels, {Model}) in {Duration}ms, {Size} bytes",
            panelCount, _model, stopwatch.Elapsed.TotalMilliseconds, imageBytes.Length);

        return imageBytes;
    }

    /// <summary>
    /// Calls <c>:generateContent</c> and extracts the inline image bytes from the first image
    /// part of the first candidate.
    /// <para>
    /// The response shape is a candidate list rather than Imagen's <c>predictions</c> array, and
    /// a candidate can legitimately come back with only text parts (the model explaining why it
    /// declined) — so the part loop looks for <c>inlineData</c> specifically instead of assuming
    /// position 0 is the image.
    /// </para>
    /// </summary>
    private async Task<byte[]> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var body = new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                // Without this the model may answer with prose about the picture it would draw.
                responseModalities = new[] { "IMAGE" }
            }
        };

        var client = _httpClientFactory.CreateClient("GeminiApi");
        var url = $"{ApiBase}/{_model}:generateContent";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Content = JsonContent.Create(body);

        _logger.LogDebug("Calling Gemini image API: {Url}", url);

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Gemini image API error {Status}: {Body}", (int)response.StatusCode, errorBody);
            throw new InvalidOperationException(
                $"Gemini image API returned {(int)response.StatusCode}: {errorBody}");
        }

        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        var root = json.RootElement;

        // A prompt rejected outright never reaches the candidate list — it comes back as a
        // promptFeedback block. Surface it with the word "blocked" so the caller's safety
        // fallback in GenerateComicImageAsync recognises it and retries with a generic prompt.
        if (root.TryGetProperty("promptFeedback", out var feedback)
            && feedback.TryGetProperty("blockReason", out var blockReason))
        {
            throw new InvalidOperationException(
                $"Gemini blocked the prompt: {blockReason.GetString()}");
        }

        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            _logger.LogWarning("Gemini returned no candidates. Raw response: {Response}", root.GetRawText());
            throw new InvalidOperationException("Gemini returned no candidates. The prompt may have been filtered.");
        }

        var candidate = candidates[0];

        if (candidate.TryGetProperty("finishReason", out var finishReason)
            && finishReason.GetString() is { } reason
            && reason is not ("STOP" or "MAX_TOKENS"))
        {
            // SAFETY / PROHIBITED_CONTENT / IMAGE_SAFETY all land here.
            throw new InvalidOperationException($"Gemini declined to draw the image: {reason}");
        }

        if (candidate.TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var parts)
            && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("inlineData", out var inlineData)
                    && inlineData.TryGetProperty("data", out var data)
                    && data.GetString() is { Length: > 0 } base64)
                {
                    return Convert.FromBase64String(base64);
                }
            }
        }

        _logger.LogWarning("Gemini returned a candidate with no image part. Raw response: {Response}", root.GetRawText());
        throw new InvalidOperationException("Gemini returned no image data for this prompt.");
    }

    private static string SanitizeNarrative(string narrative)
    {
        string[] flaggedPatterns =
        [
            "blood", "bloody", "kill", "murder", "dead", "death", "die", "dying",
            "gun", "shoot", "weapon", "knife", "stab", "fight", "attack",
            "drug", "cocaine", "heroin", "meth",
            "naked", "nude", "sex", "sexual",
            "hate", "racist", "racial",
            "vomit", "puke", "disgusting",
            "roach", "cockroach", "rat", "mice", "vermin",
            "poison", "toxic", "contaminated"
        ];

        var sanitized = narrative;
        foreach (var pattern in flaggedPatterns)
        {
            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized, $@"\b{pattern}\w*\b", "unusual",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return sanitized;
    }

    private static string BuildComicPrompt(string narrative, int panelCount)
    {
        var panelLayout = panelCount switch
        {
            1 => "Single-panorama comic strip (one wide scene filling the frame)",
            2 => "Two-panel comic strip with equal landscape panels stacked vertically",
            3 => "Three-panel strip with cinematic flow (left-to-right storytelling)",
            _ => "Four-panel comic strip arranged left-to-right, top-to-bottom (1-2 on top row, 3-4 on bottom row)"
        };

        var panelBreakdown = panelCount switch
        {
            1 => "1. Capture the most surreal moment as a cinematic snapshot with supporting background details.",
            2 => "1. Setup the unusual situation or conflict.\n2. Deliver the punchline, reaction, or outcome.",
            3 => "1. Introduce the setting and main characters.\n2. Escalate the bizarre element.\n3. Conclude with the payoff.",
            _ => "1. Setup the restaurant and characters.\n2. Introduce the strange twist.\n3. Spotlight the climax.\n4. Show the aftermath."
        };

        // Imagen has no negative-prompt channel: forbidden concepts named in the prompt
        // ("NO SPEECH BUBBLES") tend to get PAINTED INTO the artwork as literal lettering.
        // Describe only what we want — wordless, pantomime, blank surfaces — and never
        // mention text, bubbles, or writing. Captions are added later by the overlay service.
        return $"""
Create a vibrant {panelCount}-panel wordless pantomime comic strip in a clean, modern cartoon illustration style, told purely through pictures, in the tradition of silent-film slapstick.

Visual story to depict (through action and expression only):
"{narrative}"

Layout: {panelLayout}
- Consistent characters across panels with matching outfits and visual traits
- Clean black panel gutters/borders separating EXACTLY {panelCount} panel(s)

Panel breakdown:
{panelBreakdown}

Visual style:
- Bold outlines, vivid colors, exaggerated facial expressions and body language
- Modern cartoon illustration (NOT manga, NOT realistic)
- Pure visual storytelling: every emotion carried by faces, gestures, and posture alone
- Every wall, sign, menu, and surface rendered as plain solid color or simple decoration
- Wordless, silent, pantomime scenes throughout
""";
    }

    private static string BuildFallbackComicPrompt(int panelCount)
    {
        var panelLayout = panelCount switch
        {
            1 => "Single-panorama comic strip (one wide scene filling the frame)",
            2 => "Two-panel comic strip with equal landscape panels stacked vertically",
            3 => "Three-panel strip with cinematic flow (left-to-right storytelling)",
            _ => "Four-panel comic strip arranged left-to-right, top-to-bottom"
        };

        return $"""
Create a vibrant {panelCount}-panel wordless pantomime comic strip in a clean, modern cartoon illustration style, told purely through pictures, in the tradition of silent-film slapstick.

Scene: A cheerful, brightly lit restaurant. A happy customer sits at a table. A friendly waiter
brings an unusually large or creative dish. The customer reacts with wide-eyed surprise and delight.

Layout: {panelLayout}
- Bold outlines, vivid colors, exaggerated happy facial expressions
- Modern cartoon illustration style, family-friendly
- Pure visual storytelling: every emotion carried by faces, gestures, and posture alone
- Every wall, sign, menu, and surface rendered as plain solid color or simple decoration
- Wordless, silent, pantomime scenes throughout

REMINDER: This image must contain absolutely NO text, letters, words, speech bubbles, or word balloons.
""";
    }
}
