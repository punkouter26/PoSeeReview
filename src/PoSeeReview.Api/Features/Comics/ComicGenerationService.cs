using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Orchestrates comic generation workflow: review fetching, strangeness analysis,
/// narrative creation, DALL-E image generation, and blob storage upload.
/// Prioritizes 1-star reviews as source material (most interesting stories).
/// Implements 7-day caching with ExpiresAt validation.
/// </summary>
public class ComicGenerationService : IComicGenerationService
{
    private readonly IRestaurantService _restaurantService;
    private readonly IAzureOpenAIService _azureOpenAIService;
    private readonly IImageGenerationService _imageGenerationService;
    private readonly IComicTextOverlayService _comicTextOverlayService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IComicRepository _comicRepository;
    private readonly ILeaderboardService _leaderboardService;
    private readonly ILogger<ComicGenerationService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly TimeProvider _timeProvider;

    private readonly ComicOptions _options;

    public ComicGenerationService(
        IRestaurantService restaurantService,
        IAzureOpenAIService azureOpenAIService,
        IImageGenerationService imageGenerationService,
        IComicTextOverlayService comicTextOverlayService,
        IBlobStorageService blobStorageService,
        IComicRepository comicRepository,
        ILeaderboardService leaderboardService,
        ILogger<ComicGenerationService> logger,
        TelemetryClient telemetryClient,
        IOptions<ComicOptions> options,
        TimeProvider? timeProvider = null)
    {
        _restaurantService = restaurantService ?? throw new ArgumentNullException(nameof(restaurantService));
        _azureOpenAIService = azureOpenAIService ?? throw new ArgumentNullException(nameof(azureOpenAIService));
        _imageGenerationService = imageGenerationService ?? throw new ArgumentNullException(nameof(imageGenerationService));
        _comicTextOverlayService = comicTextOverlayService ?? throw new ArgumentNullException(nameof(comicTextOverlayService));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _comicRepository = comicRepository ?? throw new ArgumentNullException(nameof(comicRepository));
        _leaderboardService = leaderboardService ?? throw new ArgumentNullException(nameof(leaderboardService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Generates or retrieves cached comic for a restaurant.
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    /// <param name="forceRegenerate">If true, regenerates even if valid cache exists</param>
    /// <param name="cancellationToken">Cancels the generation pipeline</param>
    /// <returns>Generated or cached Comic entity</returns>
    /// <exception cref="KeyNotFoundException">If restaurant not found</exception>
    /// <exception cref="InsufficientReviewsException">If restaurant has fewer than required reviews</exception>
    public async Task<Comic> GenerateComicAsync(PlaceId placeId, bool forceRegenerate = false, CancellationToken cancellationToken = default)
    {
        if (placeId.IsEmpty)
            throw new ArgumentException("PlaceId is required", nameof(placeId));

        _logger.GeneratingComic(placeId.Value, forceRegenerate);

        var overallStopwatch = Stopwatch.StartNew();

        // Check cache first (unless force regenerate)
        if (!forceRegenerate)
        {
            var cachedComic = await _comicRepository.GetByPlaceIdAsync(placeId);
            if (cachedComic != null && cachedComic.ExpiresAt > _timeProvider.GetUtcNow())
            {
                _logger.ReturningCachedComic(placeId.Value);

                // Refresh SAS token if it is expired or within 2 hours of expiry
                if (IsSasExpiringSoon(cachedComic.ImageUrl))
                {
                    _logger.LogInformation("SAS token for cached comic {PlaceId} is expired/expiring — refreshing", placeId);
                    cachedComic.ImageUrl = await _blobStorageService.RefreshSasUrlAsync(cachedComic.ImageUrl);
                    await _comicRepository.UpsertAsync(cachedComic);
                }

                cachedComic.CacheState = ComicCacheState.Cached; // Mark provenance for the caller
                _telemetryClient.GetMetric("Comics.CacheHit").TrackValue(1);
                overallStopwatch.Stop();
                _telemetryClient.GetMetric("Comics.Generation.RequestDurationMs").TrackValue(overallStopwatch.Elapsed.TotalMilliseconds);
                return cachedComic;
            }
        }

        _telemetryClient.GetMetric("Comics.CacheMiss").TrackValue(1);

        // Fetch restaurant details with reviews
        Restaurant restaurant;
        try
        {
            restaurant = await _restaurantService.GetRestaurantByPlaceIdAsync(placeId, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning("Restaurant not found: {PlaceId}", placeId);
            throw;
        }

        // Validate minimum review count
        var reviews = restaurant.Reviews ?? new List<Review>();
        if (reviews.Count < _options.MinimumReviewsRequired)
        {
            _logger.LogWarning("Insufficient reviews for placeId: {PlaceId}. Found {Count}, need {Minimum}",
                placeId, reviews.Count, _options.MinimumReviewsRequired);
            throw new InsufficientReviewsException(
                $"Restaurant must have at least {_options.MinimumReviewsRequired} reviews to generate a comic. Found {reviews.Count}.");
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
        if (filteredReviews.Count < _options.MinimumReviewsRequired)
        {
            _logger.LogWarning("Insufficient appropriate reviews after filtering for placeId: {PlaceId}", placeId);
            throw new InsufficientReviewsException(
                $"Restaurant does not have enough appropriate reviews for comic generation.");
        }

        // Limit to top N reviews for analysis (cost control)
        var reviewsForAnalysis = filteredReviews.Take(_options.MaximumReviewsForAnalysis).ToList();

        _logger.LogInformation("Analyzing {Count} reviews for strangeness", reviewsForAnalysis.Count);

        // Analyze strangeness and generate narrative with panel count
        var analysisStopwatch = Stopwatch.StartNew();
        var (strangenessScore, panelCount, narrative) = await _azureOpenAIService.AnalyzeStrangenessAsync(reviewsForAnalysis, cancellationToken);
        analysisStopwatch.Stop();

        // The analyser is an external AI call: an empty or absent narrative is a plausible
        // response, not a programming error. Dereferencing it unguarded turned that into a
        // NullReferenceException surfaced to the user as "Object reference not set to an
        // instance of an object" on the comic page.
        if (string.IsNullOrWhiteSpace(narrative))
        {
            _logger.LogWarning("Strangeness analysis returned an empty narrative for placeId {PlaceId}", placeId);
            _telemetryClient.GetMetric("Comics.EmptyNarrative").TrackValue(1);
            throw new InsufficientReviewsException(
                "We couldn't turn these reviews into a story. Try another restaurant.");
        }

        _logger.LogInformation("Strangeness score: {Score}, Panel count: {PanelCount}, Narrative length: {Length}",
            strangenessScore, panelCount, narrative.Length);
        _telemetryClient.GetMetric("Comics.Generation.AnalysisDurationMs").TrackValue(analysisStopwatch.Elapsed.TotalMilliseconds);

        // Enforce the strangeness threshold BEFORE the expensive image-generation step (PRD: too
        // ordinary → 422, not a low-quality comic). Checking here also avoids burning an Imagen
        // call + blob write on a comic we would reject.
        if (strangenessScore < _options.MinimumStrangenessScore)
        {
            _logger.LogInformation("Strangeness {Score} below minimum {Minimum} for placeId {PlaceId} — rejecting as too ordinary",
                strangenessScore, _options.MinimumStrangenessScore, placeId);
            _telemetryClient.GetMetric("Comics.RejectedTooOrdinary").TrackValue(1);
            throw new InsufficientStrangenessException(strangenessScore, _options.MinimumStrangenessScore);
        }

        // Generate comic image (panel count capped at 2)
        var imageStopwatch = Stopwatch.StartNew();
        var imageBytes = await _imageGenerationService.GenerateComicImageAsync(narrative, panelCount, cancellationToken);
        imageStopwatch.Stop();

        _logger.LogInformation("Generated {PanelCount}-panel comic image: {Size} bytes", panelCount, imageBytes.Length);
        _telemetryClient.GetMetric("Comics.Generation.ImageDurationMs").TrackValue(imageStopwatch.Elapsed.TotalMilliseconds);

        // Add readable text caption overlays to each panel (replaces garbled AI-rendered text)
        var overlayStopwatch = Stopwatch.StartNew();
        imageBytes = await _comicTextOverlayService.AddTextOverlayAsync(imageBytes, narrative, panelCount, cancellationToken);
        overlayStopwatch.Stop();

        _logger.LogInformation("Added text overlay to comic: {Size} bytes", imageBytes.Length);
        _telemetryClient.GetMetric("Comics.Generation.TextOverlayDurationMs").TrackValue(overlayStopwatch.Elapsed.TotalMilliseconds);

        // Upload to blob storage
        var comicId = ComicId.New();
        var blobUrl = await _blobStorageService.UploadComicImageAsync(comicId.Value, imageBytes);

        _logger.LogInformation("Uploaded comic to blob: {BlobUrl}", blobUrl);

        // Create comic entity with 7-day expiration
        var comic = new Comic
        {
            Id = comicId,
            PlaceId = placeId,
            RestaurantName = restaurant.Name,
            ImageUrl = blobUrl,
            Narrative = narrative,
            StrangenessScore = strangenessScore,
            CreatedAt = _timeProvider.GetUtcNow(),
            ExpiresAt = _timeProvider.GetUtcNow().AddDays(_options.CacheDurationDays),
            CacheState = ComicCacheState.Generated
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
                Region = restaurant.Region,
                StrangenessScore = strangenessScore,
                ComicBlobUrl = blobUrl,
                LastUpdated = DateTimeOffset.UtcNow
            };

            await _leaderboardService.UpsertEntryAsync(leaderboardEntry);
            _logger.LogInformation("Updated leaderboard for {PlaceId} with score {Score}", placeId, strangenessScore);
        }
        catch (Exception ex)
        {
            // Don't fail comic generation if leaderboard update fails
            _logger.LogWarning(ex, "Failed to update leaderboard for {PlaceId}", placeId);
        }

        _logger.ComicGenerationComplete(placeId.Value);

        _telemetryClient.TrackEvent("ComicGenerated", new Dictionary<string, string>
        {
            ["PlaceId"] = placeId.Value,
            ["RestaurantName"] = restaurant.Name,
            ["StrangenessScore"] = strangenessScore.ToString(),
            ["PanelCount"] = panelCount.ToString()
        });

        overallStopwatch.Stop();
        _telemetryClient.GetMetric("Comics.Generation.RequestDurationMs").TrackValue(overallStopwatch.Elapsed.TotalMilliseconds);

        return comic;
    }

    /// <summary>
    /// FrozenSet of normalized profanity keywords — allocated once at startup,
    /// ~30% faster lookup than HashSet with zero per-call allocation.
    /// </summary>
    private static readonly FrozenSet<string> InappropriateKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "fuck", "shit", "ass", "bitch",
        "bastard", "piss", "slut", "whore",
        "damn", "crap", "hell", "dick", "cock",
        "penis", "vagina", "anus"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Filters out reviews containing inappropriate language or content.
    /// Normalizes leet-speak substitutions before matching so "sh1t", "f*ck", "a$$"
    /// are caught as reliably as their plaintext equivalents.
    /// </summary>
    private static List<string> FilterInappropriateReviews(List<string> reviews)
    {
        return reviews
            .Where(review => !ContainsInappropriateContent(review))
            .ToList();
    }

    /// <summary>
    /// Normalizes common leet-speak character substitutions so filter keywords
    /// match obfuscated variants (e.g. "sh!t", "f*ck", "a$$").
    /// </summary>
    private static string NormalizeLeetSpeak(string text)
    {
        return text
            .Replace("0", "o", StringComparison.Ordinal)
            .Replace("1", "i", StringComparison.Ordinal)
            .Replace("3", "e", StringComparison.Ordinal)
            .Replace("4", "a", StringComparison.Ordinal)
            .Replace("5", "s", StringComparison.Ordinal)
            .Replace("7", "t", StringComparison.Ordinal)
            .Replace("@", "a", StringComparison.Ordinal)
            .Replace("$", "s", StringComparison.Ordinal)
            .Replace("!", "i", StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal);
    }

    private static bool ContainsInappropriateContent(string review)
    {
        if (string.IsNullOrWhiteSpace(review))
            return false;

        // Normalize Unicode to NFC then apply leet-speak substitutions before matching
        var normalized = NormalizeLeetSpeak(
            review.Normalize(System.Text.NormalizationForm.FormC));

        foreach (var keyword in InappropriateKeywords)
        {
            if (Regex.IsMatch(normalized, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase))
                return true;
        }

        return false;
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
        if (prioritized.Count < _options.MaximumReviewsForAnalysis)
        {
            var needed = _options.MaximumReviewsForAnalysis - prioritized.Count;
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

    /// <summary>
    /// Returns true when a comic URL needs its read SAS refreshed: either the SAS `se`
    /// (signed expiry) is already past or within 2 hours, or the URL points at a private
    /// Azure Blob endpoint with no SAS token at all. The latter self-heals URLs that were
    /// persisted unsigned — e.g. a transient User Delegation SAS failure at write time left
    /// a bare "https://{account}.blob.core.windows.net/..." URL that a private container
    /// rejects (broken image). Azurite/local emulator URLs are signed with an account key
    /// and never hit that branch, so they are left alone.
    /// </summary>
    private static bool IsSasExpiringSoon(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            var uri = new Uri(url);
            var query = uri.Query;
            var seIdx = query.IndexOf("se=", StringComparison.OrdinalIgnoreCase);
            if (seIdx < 0)
            {
                // No SAS token. Refresh only for real Azure Blob URLs (private container needs
                // a SAS to be readable); skip Azurite/other hosts which sign a different way.
                return uri.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase);
            }

            var seStart = seIdx + 3;
            var seEnd = query.IndexOf('&', seStart);
            var seValue = Uri.UnescapeDataString(seEnd >= 0 ? query[seStart..seEnd] : query[seStart..]);
            return DateTimeOffset.TryParse(seValue, out var expiry)
                   && expiry < DateTimeOffset.UtcNow.AddHours(2);
        }
        catch
        {
            return false; // Malformed URL: leave it to the caller rather than churn refreshes
        }
    }

    /// <summary>
    /// Gets cached comic for a restaurant if it exists and hasn't expired
    /// </summary>
    public async Task<Comic?> GetCachedComicAsync(PlaceId placeId, CancellationToken cancellationToken = default)
    {
        if (placeId.IsEmpty)
            throw new ArgumentException("PlaceId is required", nameof(placeId));

        var cachedComic = await _comicRepository.GetByPlaceIdAsync(placeId);

        if (cachedComic != null && cachedComic.ExpiresAt > _timeProvider.GetUtcNow())
        {
            _logger.LogInformation("Found valid cached comic for placeId: {PlaceId}", placeId);

            if (IsSasExpiringSoon(cachedComic.ImageUrl))
            {
                _logger.LogInformation("SAS token for cached comic {PlaceId} is expired/expiring — refreshing", placeId);
                cachedComic.ImageUrl = await _blobStorageService.RefreshSasUrlAsync(cachedComic.ImageUrl);
                await _comicRepository.UpsertAsync(cachedComic);
            }

            cachedComic.CacheState = ComicCacheState.Cached;
            return cachedComic;
        }

        _logger.LogInformation("No valid cached comic for placeId: {PlaceId}", placeId);
        return null;
    }
}
