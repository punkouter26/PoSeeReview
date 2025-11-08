using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Po.SeeReview.Core.Entities;
using Po.SeeReview.Core.Interfaces;

namespace Po.SeeReview.Infrastructure.Services;

/// <summary>
/// Orchestrates comic generation workflow: review fetching, strangeness analysis,
/// narrative creation, DALL-E image generation, and blob storage upload.
/// Prioritizes 1-star reviews as source material (most interesting stories).
/// Implements 24-hour caching with ExpiresAt validation.
/// </summary>
public class ComicGenerationService : IComicGenerationService
{
    private readonly IRestaurantService _restaurantService;
    private readonly IAzureOpenAIService _azureOpenAIService;
    private readonly IDalleComicService _dalleComicService;
    private readonly IComicTextOverlayService _comicTextOverlayService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IComicRepository _comicRepository;
    private readonly ILeaderboardService _leaderboardService;
    private readonly ILogger<ComicGenerationService> _logger;
    private readonly TelemetryClient _telemetryClient;

    private const int MinimumReviewsRequired = 5;
    private const int MaximumReviewsForAnalysis = 5; // Reduced from 10 to cut GPT costs in half
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(7); // Extended from 24h to reduce AI costs

    public ComicGenerationService(
        IRestaurantService restaurantService,
        IAzureOpenAIService azureOpenAIService,
        IDalleComicService dalleComicService,
        IComicTextOverlayService comicTextOverlayService,
        IBlobStorageService blobStorageService,
        IComicRepository comicRepository,
        ILeaderboardService leaderboardService,
        ILogger<ComicGenerationService> logger,
        TelemetryClient telemetryClient)
    {
        _restaurantService = restaurantService ?? throw new ArgumentNullException(nameof(restaurantService));
        _azureOpenAIService = azureOpenAIService ?? throw new ArgumentNullException(nameof(azureOpenAIService));
        _dalleComicService = dalleComicService ?? throw new ArgumentNullException(nameof(dalleComicService));
        _comicTextOverlayService = comicTextOverlayService ?? throw new ArgumentNullException(nameof(comicTextOverlayService));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _comicRepository = comicRepository ?? throw new ArgumentNullException(nameof(comicRepository));
        _leaderboardService = leaderboardService ?? throw new ArgumentNullException(nameof(leaderboardService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
    }

    /// <summary>
    /// Generates or retrieves cached comic for a restaurant.
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    /// <param name="forceRegenerate">If true, regenerates even if valid cache exists</param>
    /// <returns>Generated or cached Comic entity</returns>
    /// <exception cref="KeyNotFoundException">If restaurant not found</exception>
    /// <exception cref="InvalidOperationException">If restaurant has fewer than 5 reviews</exception>
    public async Task<Comic> GenerateComicAsync(string placeId, bool forceRegenerate = false)
    {
        if (string.IsNullOrWhiteSpace(placeId))
            throw new ArgumentNullException(nameof(placeId));

        _logger.LogInformation("Generating comic for placeId: {PlaceId}, forceRegenerate: {ForceRegenerate}",
            placeId, forceRegenerate);

        var overallStopwatch = Stopwatch.StartNew();

        // Check cache first (unless force regenerate)
        if (!forceRegenerate)
        {
            var cachedComic = await _comicRepository.GetByPlaceIdAsync(placeId);
            if (cachedComic != null && cachedComic.ExpiresAt > DateTime.UtcNow)
            {
                _logger.LogInformation("Returning cached comic for placeId: {PlaceId}", placeId);
                cachedComic.IsCached = true; // Mark as cached for caller
                _telemetryClient.GetMetric("Comics.CacheHit").TrackValue(1);
                overallStopwatch.Stop();
                _telemetryClient.GetMetric("Comics.Generation.RequestDurationMs").TrackValue(overallStopwatch.Elapsed.TotalMilliseconds);
                return cachedComic;
            }
        }

        _telemetryClient.GetMetric("Comics.CacheMiss").TrackValue(1);

        // Fetch restaurant details with reviews
        var restaurant = await _restaurantService.GetRestaurantDetailsAsync(placeId);
        if (restaurant == null)
        {
            _logger.LogWarning("Restaurant not found: {PlaceId}", placeId);
            throw new KeyNotFoundException($"Restaurant not found: {placeId}");
        }

        // Validate minimum review count
        var reviews = restaurant.Reviews ?? new List<Review>();
        if (reviews.Count < MinimumReviewsRequired)
        {
            _logger.LogWarning("Insufficient reviews for placeId: {PlaceId}. Found {Count}, need {Minimum}",
                placeId, reviews.Count, MinimumReviewsRequired);
            throw new InvalidOperationException(
                $"Restaurant must have at least {MinimumReviewsRequired} reviews to generate a comic. Found {reviews.Count}.");
        }

        // Prioritize 1-star reviews (most interesting), then add higher ratings if needed
        var prioritizedReviews = PrioritizeReviewsByRating(reviews);
        var reviewTexts = prioritizedReviews.Select(r => r.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

        _logger.LogInformation("Selected {OneStarCount} one-star reviews, {TwoStarCount} two-star reviews, {OtherCount} other reviews",
            prioritizedReviews.Count(r => r.Rating == 1),
            prioritizedReviews.Count(r => r.Rating == 2),
            prioritizedReviews.Count(r => r.Rating > 2));

        // Filter inappropriate content
        var filteredReviews = FilterInappropriateReviews(reviewTexts);
        if (filteredReviews.Count < MinimumReviewsRequired)
        {
            _logger.LogWarning("Insufficient appropriate reviews after filtering for placeId: {PlaceId}", placeId);
            throw new InvalidOperationException(
                $"Restaurant does not have enough appropriate reviews for comic generation.");
        }

        // Limit to top N reviews for analysis (cost control)
        var reviewsForAnalysis = filteredReviews.Take(MaximumReviewsForAnalysis).ToList();

        _logger.LogInformation("Analyzing {Count} reviews for strangeness", reviewsForAnalysis.Count);

        // Analyze strangeness and generate narrative with panel count
        var analysisStopwatch = Stopwatch.StartNew();
        var (strangenessScore, panelCount, narrative) = await _azureOpenAIService.AnalyzeStrangenessAsync(reviewsForAnalysis);
        analysisStopwatch.Stop();

        _logger.LogInformation("Strangeness score: {Score}, Panel count: {PanelCount}, Narrative length: {Length}",
            strangenessScore, panelCount, narrative.Length);
        _telemetryClient.GetMetric("Comics.Generation.AnalysisDurationMs").TrackValue(analysisStopwatch.Elapsed.TotalMilliseconds);

        // Generate comic image with DALL-E (panel count between 1-4)
        var imageStopwatch = Stopwatch.StartNew();
        var imageBytes = await _dalleComicService.GenerateComicImageAsync(narrative, panelCount);
        imageStopwatch.Stop();

        _logger.LogInformation("Generated {PanelCount}-panel comic image: {Size} bytes", panelCount, imageBytes.Length);
        _telemetryClient.GetMetric("Comics.Generation.ImageDurationMs").TrackValue(imageStopwatch.Elapsed.TotalMilliseconds);

        // Add text overlay to comic (fixes DALL-E's gibberish text)
        var overlayStopwatch = Stopwatch.StartNew();
        imageBytes = await _comicTextOverlayService.AddTextOverlayAsync(imageBytes, narrative, panelCount);
        overlayStopwatch.Stop();

        _logger.LogInformation("Added text overlay to comic: {Size} bytes", imageBytes.Length);
        _telemetryClient.GetMetric("Comics.Generation.TextOverlayDurationMs").TrackValue(overlayStopwatch.Elapsed.TotalMilliseconds);

        // Upload to blob storage
        var comicId = Guid.NewGuid().ToString();
        var blobUrl = await _blobStorageService.UploadComicImageAsync(comicId, imageBytes);

        _logger.LogInformation("Uploaded comic to blob: {BlobUrl}", blobUrl);

        // Create comic entity with 24-hour expiration
        var comic = new Comic
        {
            Id = comicId,
            PlaceId = placeId,
            RestaurantName = restaurant.Name,
            ImageUrl = blobUrl,
            Narrative = narrative,
            StrangenessScore = strangenessScore,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(CacheDuration),
            IsCached = false
        };

        // Save to cache
        await _comicRepository.UpsertAsync(comic);

        // Update leaderboard if score is high enough (service handles threshold check)
        try
        {
            var leaderboardEntry = new LeaderboardEntry
            {
                PlaceId = placeId,
                RestaurantName = restaurant.Name,
                Address = restaurant.Address,
                Region = restaurant.Region ?? "US",
                StrangenessScore = strangenessScore,
                ComicBlobUrl = blobUrl,
                LastUpdated = DateTime.UtcNow
            };

            await _leaderboardService.UpsertEntryAsync(leaderboardEntry);
            _logger.LogInformation("Updated leaderboard for {PlaceId} with score {Score}", placeId, strangenessScore);
        }
        catch (Exception ex)
        {
            // Don't fail comic generation if leaderboard update fails
            _logger.LogWarning(ex, "Failed to update leaderboard for {PlaceId}", placeId);
        }

        _logger.LogInformation("Comic generation complete for placeId: {PlaceId}", placeId);

        _telemetryClient.TrackEvent("ComicGenerated", new Dictionary<string, string>
        {
            ["PlaceId"] = placeId,
            ["RestaurantName"] = restaurant.Name,
            ["StrangenessScore"] = strangenessScore.ToString(),
            ["PanelCount"] = panelCount.ToString()
        });

        overallStopwatch.Stop();
        _telemetryClient.GetMetric("Comics.Generation.RequestDurationMs").TrackValue(overallStopwatch.Elapsed.TotalMilliseconds);

        return comic;
    }

    /// <summary>
    /// Filters out reviews containing inappropriate language or content.
    /// Simple keyword-based filtering per FR-015.
    /// </summary>
    private List<string> FilterInappropriateReviews(List<string> reviews)
    {
        // Basic profanity filter - only severe profanity (expand as needed)
        var inappropriateKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fuck", "shit", "ass", "bitch",
            "bastard", "piss", "slut", "whore"
        };

        return reviews
            .Where(review => !ContainsInappropriateContent(review, inappropriateKeywords))
            .ToList();
    }

    /// <summary>
    /// Prioritizes reviews by rating for comic generation.
    /// Focuses EXCLUSIVELY on negative reviews (1-3 stars) when available.
    /// NOTE: Google Maps API only returns 5 reviews maximum - we cannot access all 1000+ reviews.
    /// </summary>
    /// <param name="reviews">List of all reviews (typically only 5 from Google API)</param>
    /// <returns>Prioritized list with ALL negative reviews first, positive only as fallback</returns>
    private List<Review> PrioritizeReviewsByRating(List<Review> reviews)
    {
        // Separate negative (1-3 stars) and positive (4-5 stars) reviews
        var negativeReviews = reviews.Where(r => r.Rating <= 3).ToList();
        var positiveReviews = reviews.Where(r => r.Rating >= 4).ToList();

        // Sort each group by length (more content is better)
        var sortedNegative = negativeReviews
            .OrderBy(r => r.Rating) // 1-star first, then 2, then 3
            .ThenByDescending(r => r.Text?.Length ?? 0)
            .ToList();

        var sortedPositive = positiveReviews
            .OrderBy(r => r.Rating) // 4-star before 5-star
            .ThenByDescending(r => r.Text?.Length ?? 0)
            .ToList();

        // Build final list: ALL negative reviews first, then positive as fallback
        var prioritized = new List<Review>();
        prioritized.AddRange(sortedNegative);

        // Only add positive reviews if we need more to reach minimum
        if (prioritized.Count < MaximumReviewsForAnalysis)
        {
            var needed = MaximumReviewsForAnalysis - prioritized.Count;
            prioritized.AddRange(sortedPositive.Take(needed));
        }

        _logger.LogWarning("⚠️ Google API limitation: Only {TotalCount} reviews available (not all {TotalReviewsText}). Using {NegativeCount} negative (1-3★) and {PositiveCount} positive (4-5★)",
            reviews.Count,
            "1000+",
            prioritized.Count(r => r.Rating <= 3),
            prioritized.Count(r => r.Rating >= 4));

        _logger.LogInformation("Review breakdown: {OneStarCount} 1★, {TwoStarCount} 2★, {ThreeStarCount} 3★, {FourStarCount} 4★, {FiveStarCount} 5★",
            prioritized.Count(r => r.Rating == 1),
            prioritized.Count(r => r.Rating == 2),
            prioritized.Count(r => r.Rating == 3),
            prioritized.Count(r => r.Rating == 4),
            prioritized.Count(r => r.Rating == 5));

        return prioritized;
    }

    private static bool ContainsInappropriateContent(string review, HashSet<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(review))
            return false;

        // Simple word boundary check
        var words = review.Split(new[] { ' ', '.', ',', '!', '?', ';', ':' },
            StringSplitOptions.RemoveEmptyEntries);

        return words.Any(word => keywords.Contains(word.Trim()));
    }

    /// <summary>
    /// Gets cached comic for a restaurant if it exists and hasn't expired
    /// </summary>
    public async Task<Comic?> GetCachedComicAsync(string placeId)
    {
        if (string.IsNullOrWhiteSpace(placeId))
            throw new ArgumentNullException(nameof(placeId));

        var cachedComic = await _comicRepository.GetByPlaceIdAsync(placeId);

        if (cachedComic != null && cachedComic.ExpiresAt > DateTime.UtcNow)
        {
            _logger.LogInformation("Found valid cached comic for placeId: {PlaceId}", placeId);
            cachedComic.IsCached = true;
            return cachedComic;
        }

        _logger.LogInformation("No valid cached comic for placeId: {PlaceId}", placeId);
        return null;
    }
}
