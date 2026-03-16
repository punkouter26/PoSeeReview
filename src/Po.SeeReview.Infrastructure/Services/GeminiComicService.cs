using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Po.SeeReview.Core.Interfaces;
using Polly;
using Polly.Retry;

namespace Po.SeeReview.Infrastructure.Services;

/// <summary>
/// Google Gemini image generation service using Imagen 4.
/// Uses the Generative Language REST API.
/// Requires <c>Google:GeminiApiKey</c> in configuration (stored as "PoSeeReview--Google--GeminiApiKey" in Key Vault).
/// </summary>
public sealed class GeminiComicService : IImageGenerationService
{
    // Imagen 4 Fast: good quality, lower latency, uses :predict endpoint
    private const string DefaultModel = "imagen-4.0-fast-generate-001";
    private const string ApiBase = "https://generativelanguage.googleapis.com/v1beta/models";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GeminiComicService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly AsyncRetryPolicy<byte[]> _retryPolicy;

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

        _logger.LogInformation("GeminiComicService initialised. Model: {Model} (using Imagen :predict endpoint)", _model);

        // Retry on transient HTTP failures (429 rate-limit, 503 overload) with exponential back-off
        _retryPolicy = Policy<byte[]>
            .Handle<HttpRequestException>()
            .Or<InvalidOperationException>(ex => ex.Message.Contains("503") || ex.Message.Contains("429") || ex.Message.Contains("RESOURCE_EXHAUSTED"))
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (outcome, delay, attempt, _) =>
                    _logger.LogWarning(
                        "Gemini image generation retry {Attempt} after {Delay}s. Reason: {Reason}",
                        attempt, delay.TotalSeconds, outcome.Exception?.Message ?? "unknown"));
    }

    /// <inheritdoc />
    public async Task<byte[]> GenerateComicImageAsync(string narrative, int panelCount)
    {
        if (string.IsNullOrWhiteSpace(narrative))
            throw new ArgumentException("Narrative cannot be empty", nameof(narrative));

        if (panelCount is < 1 or > 4)
            throw new ArgumentException("Panel count must be between 1 and 4", nameof(panelCount));

        var stopwatch = Stopwatch.StartNew();
        var prompt = BuildComicPrompt(SanitizeNarrative(narrative), panelCount);

        byte[] imageBytes;
        try
        {
            imageBytes = await _retryPolicy.ExecuteAsync(
                ct => GenerateAsync(prompt, ct), CancellationToken.None);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("safety", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("declined", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("blocked", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Gemini blocked content, falling back to generic comic prompt");
            imageBytes = await _retryPolicy.ExecuteAsync(
                ct => GenerateAsync(BuildFallbackComicPrompt(panelCount), ct), CancellationToken.None);
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
    /// Calls the Imagen :predict API and extracts the base64 image bytes from the response.
    /// Compatible with imagen-3.0-generate-002, imagen-4.0-fast-generate-001, imagen-4.0-generate-001, etc.
    /// </summary>
    private async Task<byte[]> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var body = new
        {
            instances = new[] { new { prompt } },
            parameters = new
            {
                sampleCount = 1,
                aspectRatio = "1:1",
                safetyFilterLevel = "block_some",
                personGeneration = "allow_adult"
            }
        };

        var client = _httpClientFactory.CreateClient("GeminiApi");
        var url = $"{ApiBase}/{_model}:predict";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Content = JsonContent.Create(body);

        _logger.LogDebug("Calling Imagen API: {Url}", url);

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Imagen API error {Status}: {Body}", (int)response.StatusCode, errorBody);
            throw new InvalidOperationException(
                $"Imagen API returned {(int)response.StatusCode}: {errorBody}");
        }

        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        var prediction = json.RootElement
            .GetProperty("predictions")
            .EnumerateArray()
            .FirstOrDefault();

        if (prediction.ValueKind == JsonValueKind.Undefined)
        {
            var rawJson = json.RootElement.GetRawText();
            _logger.LogWarning("Imagen returned empty predictions. Raw response: {Response}", rawJson);
            throw new InvalidOperationException(
                "Imagen returned no predictions. The prompt may have been filtered.");
        }

        var imageBase64 = prediction.GetProperty("bytesBase64Encoded").GetString()
            ?? throw new InvalidOperationException("Imagen response missing bytesBase64Encoded field");

        return Convert.FromBase64String(imageBase64);
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

        return $"""
CRITICAL CONSTRAINT — THIS IS A SILENT COMIC WITH ABSOLUTELY NO TEXT IN THE IMAGE:
- DO NOT draw speech bubbles or word balloons of any shape or size
- DO NOT draw thought bubbles
- DO NOT write any dialogue, captions, signs, labels, or letters anywhere
- DO NOT render any text, symbols, or writing — even decorative or background text
- Characters may open their mouths to speak but NO bubble or text appears
- Leave all space around characters completely clean and empty

Create a vibrant {panelCount}-panel comic strip in a clean, modern illustration style.

Story context:
"{narrative}"

Layout: {panelLayout}
- Consistent characters across panels with matching outfits and visual traits
- Clean black panel gutters/borders separating EXACTLY {panelCount} panel(s)

Panel breakdown:
{panelBreakdown}

Visual style:
- Bold outlines, vivid colors, exaggerated facial expressions
- Modern cartoon illustration (NOT manga, NOT realistic)
- Story told entirely through actions, expressions, and body language
- Background walls/signs must be plain — no readable text on them

REMEMBER: Zero text. Zero speech bubbles. Zero word balloons. Zero letters. Silent comic only.
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
ABSOLUTE RULE: NO TEXT. NO SPEECH BUBBLES. NO WORD BALLOONS. NO CAPTIONS. NO LETTERS. NO WRITING OF ANY KIND.

Create a vibrant {panelCount}-panel comic strip in a clean, modern cartoon illustration style.

Scene: A cheerful, brightly lit restaurant. A happy customer sits at a table. A friendly waiter
brings an unusually large or creative dish. The customer reacts with wide-eyed surprise and delight.

Layout: {panelLayout}
- Bold outlines, vivid colors, exaggerated happy facial expressions
- Modern cartoon illustration style, family-friendly
- ZERO text, ZERO speech bubbles, ZERO word balloons, ZERO labels, ZERO writing anywhere
- Characters communicate ONLY through facial expressions and body language

REMINDER: This image must contain absolutely NO text, letters, words, speech bubbles, or word balloons.
""";
    }
}
