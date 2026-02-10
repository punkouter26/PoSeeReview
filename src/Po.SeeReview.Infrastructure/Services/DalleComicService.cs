using System.ClientModel;
using System.Diagnostics;
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
/// Azure OpenAI DALL-E 3 service for generating 1-4 panel comic strip images from narratives.
/// Uses the Azure.AI.OpenAI SDK with the Images API.
/// Generates 1024x1024 square images at Standard quality for minimum cost ($0.04/image).
/// Panel layout adapts based on count: 1 panel (full), 2 panels (side-by-side), 3-4 panels (grid).
/// Text overlay is added by ComicTextOverlayService after image generation.
/// </summary>
public class DalleComicService : IDalleComicService
{
    private readonly AzureOpenAIClient _openAIClient;
    private readonly HttpClient _httpClient;
    private readonly string _deploymentName;
    private readonly ILogger<DalleComicService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly AsyncRetryPolicy<byte[]> _imageRetryPolicy;

    public DalleComicService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DalleComicService> logger,
        TelemetryClient telemetryClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        // Use dedicated image generation endpoint if configured, otherwise fall back to primary endpoint
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
            ?? throw new InvalidOperationException("Image generation deployment name not configured");

        _openAIClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));

        _logger.LogInformation("DalleComicService configured with deployment: {Deployment}, endpoint: {Endpoint}",
            _deploymentName, endpoint);

        _imageRetryPolicy = Policy<byte[]>
            .Handle<RequestFailedException>(AzureRetryUtils.IsTransientFailure)
            .Or<HttpRequestException>()
            .Or<InvalidOperationException>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (outcome, timespan, attempt, _) =>
            {
                var reason = outcome.Exception?.Message ?? "unknown";
                _logger.LogWarning(
                    "Retrying image generation due to {Reason}. Attempt {Attempt}. Waiting {Delay} seconds",
                    reason,
                    attempt,
                    timespan.TotalSeconds);
            });
    }

    /// <summary>
    /// Generates a comic strip image using DALL-E 3 at Standard quality (cheapest: $0.04/image).
    /// </summary>
    /// <param name="narrative">Story narrative for the comic</param>
    /// <param name="panelCount">Number of panels (1-4)</param>
    /// <returns>PNG image bytes (1024x1024 square format)</returns>
    /// <exception cref="ArgumentException">If narrative is empty or panelCount invalid</exception>
    /// <exception cref="InvalidOperationException">If image generation fails</exception>
    public async Task<byte[]> GenerateComicImageAsync(string narrative, int panelCount)
    {
        if (string.IsNullOrWhiteSpace(narrative))
            throw new ArgumentException("Narrative cannot be empty", nameof(narrative));

        if (panelCount < 1 || panelCount > 4)
            throw new ArgumentException("Panel count must be between 1 and 4", nameof(panelCount));

        // DALL-E 3: Standard quality + 1024x1024 = $0.04/image (cheapest option)
        var imageOptions = new ImageGenerationOptions
        {
            Size = GeneratedImageSize.W1024xH1024,
            Style = GeneratedImageStyle.Vivid
        };

        var stopwatch = Stopwatch.StartNew();

        // Try with sanitized narrative first, fall back to generic prompt on content policy violation
        var sanitizedNarrative = SanitizeNarrative(narrative);
        var prompt = BuildComicPrompt(sanitizedNarrative, panelCount);

        byte[] imageBytes;
        try
        {
            imageBytes = await _imageRetryPolicy.ExecuteAsync(
                () => GenerateImageFromPromptAsync(prompt, imageOptions));
        }
        catch (ClientResultException ex) when (ex.Message.Contains("content_policy_violation", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("contentFilter", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Content policy violation with sanitized narrative, falling back to generic prompt");
            var fallbackPrompt = BuildFallbackComicPrompt(panelCount);
            imageBytes = await _imageRetryPolicy.ExecuteAsync(
                () => GenerateImageFromPromptAsync(fallbackPrompt, imageOptions));
        }

        stopwatch.Stop();

        _telemetryClient.GetMetric("AzureOpenAI.Image.Requests").TrackValue(1);
        _telemetryClient.GetMetric("AzureOpenAI.Image.DurationMs").TrackValue(stopwatch.Elapsed.TotalMilliseconds);
        _telemetryClient.GetMetric("AzureOpenAI.Image.CostUsd").TrackValue(0.04);

        _logger.LogInformation(
            "Generated comic image ({PanelCount} panels, {Deployment}, ~$0.04) in {Duration}ms",
            panelCount,
            _deploymentName,
            stopwatch.Elapsed.TotalMilliseconds);

        return imageBytes;
    }

    /// <summary>
    /// Generates an image from a prompt and downloads the result.
    /// DALL-E 3 returns a temporary URL; we download the image bytes via HttpClient.
    /// </summary>
    private async Task<byte[]> GenerateImageFromPromptAsync(string prompt, ImageGenerationOptions imageOptions)
    {
        var imageClient = _openAIClient.GetImageClient(_deploymentName);
        var response = await imageClient.GenerateImageAsync(prompt, imageOptions);
        var imageUri = response.Value.ImageUri;

        if (imageUri == null)
        {
            throw new InvalidOperationException("Image generation returned no URI");
        }

        return await _httpClient.GetByteArrayAsync(imageUri);
    }

    /// <summary>
    /// Strips potentially flagged content from the narrative to avoid content policy violations.
    /// Removes profanity, violence references, and other sensitive terms.
    /// </summary>
    private static string SanitizeNarrative(string narrative)
    {
        // Remove common content-policy-triggering words/phrases
        var sanitized = narrative;
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

        foreach (var pattern in flaggedPatterns)
        {
            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized, $@"\b{pattern}\w*\b", "unusual", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return sanitized;
    }

    /// <summary>
    /// Builds a safe generic comic prompt when the narrative-based prompt is rejected.
    /// </summary>
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
            Create a vibrant {panelCount}-panel comic strip in a clean, modern cartoon illustration style.

            Scene: A cheerful, brightly lit restaurant. A happy customer sits at a table. A friendly waiter
            brings an unusually large or creative dish. The customer reacts with wide-eyed surprise and delight.

            Layout: {panelLayout}
            - Bold outlines, vivid colors, exaggerated happy facial expressions
            - Modern cartoon illustration style, family-friendly
            - Do not include any text, speech bubbles, labels, or writing anywhere in the image
            - Leave clear empty space in each panel for text to be added later
            """;
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

REQUIREMENTS:
1. Create EXACTLY {panelCount} panel(s)
2. Do NOT draw any text, speech bubbles, word balloons, captions, labels, signs, or writing anywhere
3. This is a SILENT COMIC - tell the story purely through visuals

Story context:
"{narrative}"

Layout: {panelLayout}
- Consistent characters across panels with matching outfits and visual traits
- Clean panel gutters/borders separating EXACTLY {panelCount} panel(s)

Panel breakdown:
{panelBreakdown}

Visual style:
- 1024x1024 square with {panelCount} distinct panels
- Bold outlines, vivid colors, exaggerated facial expressions
- Modern cartoon illustration (NOT manga, NOT realistic)
- No text, no speech bubbles, no labels, no signs with writing
- Leave clear empty space in each panel for text overlay
- Tell the story through actions, expressions, and body language only
""";
    }
}
