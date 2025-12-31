using System.ClientModel;
using System.Diagnostics;
using System.Net.Http;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Images;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Core.Utilities;
using Polly;
using Polly.Retry;

namespace Po.SeeReview.Infrastructure.Services;

/// <summary>
/// Azure AI Foundry DALL-E 3 service for generating 1-4 panel comic strip images from narratives.
/// Uses the Azure.AI.OpenAI SDK to connect to Azure AI Foundry (Cognitive Services).
/// Generates 1024x1024 square images with vivid, cartoon style.
/// Panel layout adapts based on count: 1 panel (full), 2 panels (side-by-side), 3-4 panels (grid).
/// Cost optimized at $0.040 per image (50% cheaper than 1792x1024).
/// IMPORTANT: DALL-E cannot generate correct English text, so prompts explicitly prohibit any text/speech bubbles.
/// Text overlay is added by ComicTextOverlayService after image generation.
/// </summary>
public class DalleComicService : IDalleComicService
{
    private readonly AzureOpenAIClient _openAIClient;
    private readonly string _deploymentName;
    private readonly HttpClient _httpClient;
    private readonly ILogger<DalleComicService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly AsyncRetryPolicy<byte[]> _imageRetryPolicy;

    public DalleComicService(
        IConfiguration configuration,
        HttpClient httpClient,
        ILogger<DalleComicService> logger,
        TelemetryClient telemetryClient)
    {
        // Use dedicated DALL-E endpoint if configured, otherwise fall back to primary endpoint
        var dalleEndpoint = configuration["AzureOpenAI:DalleEndpoint"];
        var dalleApiKey = configuration["AzureOpenAI:DalleApiKey"];

        var endpoint = !string.IsNullOrEmpty(dalleEndpoint)
            ? dalleEndpoint
            : configuration["AzureOpenAI:Endpoint"]
                ?? throw new InvalidOperationException("Azure OpenAI endpoint not configured");

        var apiKey = !string.IsNullOrEmpty(dalleApiKey)
            ? dalleApiKey
            : configuration["AzureOpenAI:ApiKey"]
                ?? throw new InvalidOperationException("Azure OpenAI API key not configured");

        _deploymentName = configuration["AzureOpenAI:DalleDeploymentName"]
            ?? throw new InvalidOperationException("DALL-E deployment name not configured");

        _openAIClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        _httpClient = httpClient;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));

        _logger.LogInformation("DalleComicService configured with endpoint: {Endpoint}", endpoint);

        _imageRetryPolicy = Policy<byte[]>
            .Handle<RequestFailedException>(AzureRetryUtils.IsTransientFailure)
            .Or<HttpRequestException>()
            .Or<InvalidOperationException>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (outcome, timespan, attempt, _) =>
            {
                var reason = outcome.Exception?.Message ?? "unknown";
                _logger.LogWarning(
                    "Retrying DALL-E image operation due to {Reason}. Attempt {Attempt}. Waiting {Delay} seconds",
                    reason,
                    attempt,
                    timespan.TotalSeconds);
            });
    }

    /// <summary>
    /// Generates a comic strip image from a narrative using DALL-E 3.
    /// </summary>
    /// <param name="narrative">Story narrative for the comic</param>
    /// <param name="panelCount">Number of panels (1-4)</param>
    /// <returns>PNG image bytes (1024x1024 square format)</returns>
    /// <exception cref="ArgumentException">If narrative is empty or panelCount invalid</exception>
    /// <exception cref="InvalidOperationException">If image generation or download fails</exception>
    public async Task<byte[]> GenerateComicImageAsync(string narrative, int panelCount)
    {
        if (string.IsNullOrWhiteSpace(narrative))
            throw new ArgumentException("Narrative cannot be empty", nameof(narrative));

        if (panelCount < 1 || panelCount > 4)
            throw new ArgumentException("Panel count must be between 1 and 4", nameof(panelCount));

        var prompt = BuildComicPrompt(narrative, panelCount);
        var imageOptions = new ImageGenerationOptions
        {
            Size = GeneratedImageSize.W1024xH1024, // Reduced from 1792x1024 to save 50% cost ($0.040 vs $0.080)
            Quality = GeneratedImageQuality.Standard,
            Style = GeneratedImageStyle.Vivid
        };

        var stopwatch = Stopwatch.StartNew();

        var imageBytes = await _imageRetryPolicy.ExecuteAsync(async () =>
        {
            var imageClient = _openAIClient.GetImageClient(_deploymentName);
            var response = await imageClient.GenerateImageAsync(prompt, imageOptions);
            var imageUrl = response.Value.ImageUri;

            if (imageUrl == null)
            {
                throw new InvalidOperationException("DALL-E did not return an image URL");
            }

            var downloadedBytes = await _httpClient.GetByteArrayAsync(imageUrl);

            if (downloadedBytes.Length == 0)
            {
                throw new InvalidOperationException("Downloaded image is empty");
            }

            return downloadedBytes;
        });

        stopwatch.Stop();

        _telemetryClient.GetMetric("AzureOpenAI.Image.Requests").TrackValue(1);
        _telemetryClient.GetMetric("AzureOpenAI.Image.EstimatedCostUsd").TrackValue(0.04); // Standard quality pricing per image
        _telemetryClient.GetMetric("AzureOpenAI.Image.DurationMs").TrackValue(stopwatch.Elapsed.TotalMilliseconds);

        _logger.LogInformation(
            "Generated DALL-E comic image ({PanelCount} panels) in {Duration}ms",
            panelCount,
            stopwatch.Elapsed.TotalMilliseconds);

        return imageBytes;
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
            2 => "1. Setup the unusual situation or conflict.\n2. Deliver the punchline, reaction, or outcome with expressive characters.",
            3 => "1. Introduce the setting and main characters.\n2. Escalate the bizarre or unexpected element.\n3. Conclude with the payoff or lingering reaction.",
            _ => "1. Setup the restaurant and characters (establish the normal world).\n2. Introduce the strange or unsettling twist.\n3. Spotlight the climax or most absurd detail.\n4. Show the aftermath or characters processing what happened."
        };

        return $"""
Create a vibrant {panelCount}-panel comic strip in a clean, modern illustration style.

CRITICAL REQUIREMENTS:
1. Create EXACTLY {panelCount} panel(s), no more, no less
2. NEVER EVER draw speech bubbles, word balloons, text boxes, or any text elements
3. DO NOT attempt to write any letters, words, signs, labels, or typography anywhere in the image
4. This is a SILENT COMIC - tell the story through visuals only, no text permitted

Story context (draw inspiration from this narrative):
"{narrative}"

Layout: {panelLayout}
- Maintain consistent characters across panels with matching outfits and visual traits
- Clean panel gutters/borders separating EXACTLY {panelCount} panel(s)

Panel breakdown (create ONLY {panelCount} panels):
{panelBreakdown}

Visual requirements:
- 1792x1024 resolution with {panelCount} distinct panels
- Bold outlines, vivid colors, slightly exaggerated facial expressions
- Modern comic illustration style (NOT manga, NOT realistic)
- Minimal but expressive backgrounds
- Tell the entire story through CHARACTER ACTIONS, FACIAL EXPRESSIONS, and BODY LANGUAGE ONLY
- NO speech bubbles, NO thought bubbles, NO text balloons, NO captions, NO labels, NO signs with text
- Leave clear empty space in each panel (preferably top or bottom third) for text to be added later
- Characters should have open mouths if talking, but NO speech bubbles around them
- Avoid any symbols, letters, or writing of any kind
- Focus on comedic visual storytelling without words

ABSOLUTELY FORBIDDEN:
❌ Speech bubbles
❌ Word balloons  
❌ Thought bubbles
❌ Text boxes
❌ Captions
❌ Letters or words anywhere
❌ Signs with text
❌ Sound effects written out
❌ Any typography or written language

REMEMBER: This is a SILENT visual comic. Text and dialogue will be added later as an overlay. Generate EXACTLY {panelCount} panel(s) with NO text elements whatsoever.
""";
    }
}
