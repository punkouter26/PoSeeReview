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
public partial class ComicGenerationService : IComicGenerationService
{
    private readonly IRestaurantService _restaurantService;
    private readonly IChatCompletionService _chatService;
    private readonly IImageGenerationService _imageGenerationService;
    private readonly IComicTextOverlayService _comicTextOverlayService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IComicRepository _comicRepository;
    private readonly ILeaderboardService _leaderboardService;
    private readonly IContentSafetyScreener _contentSafetyScreener;
    private readonly IContentModerationGate _moderationGate;
    private readonly ILogger<ComicGenerationService> _logger;
    private readonly TelemetryClient _telemetryClient;
    private readonly TimeProvider _timeProvider;
    private readonly ComicGenerationLock _generationLock;
    private readonly IEmbeddingService _embeddingService;

    private readonly ComicOptions _options;

    public ComicGenerationService(
        IRestaurantService restaurantService,
        IChatCompletionService chatService,
        IImageGenerationService imageGenerationService,
        IComicTextOverlayService comicTextOverlayService,
        IBlobStorageService blobStorageService,
        IComicRepository comicRepository,
        ILeaderboardService leaderboardService,
        IContentSafetyScreener contentSafetyScreener,
        IContentModerationGate moderationGate,
        ILogger<ComicGenerationService> logger,
        TelemetryClient telemetryClient,
        ComicGenerationLock generationLock,
        IEmbeddingService embeddingService,
        IOptions<ComicOptions> options,
        TimeProvider? timeProvider = null)
    {
        _restaurantService = restaurantService ?? throw new ArgumentNullException(nameof(restaurantService));
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        _imageGenerationService = imageGenerationService ?? throw new ArgumentNullException(nameof(imageGenerationService));
        _comicTextOverlayService = comicTextOverlayService ?? throw new ArgumentNullException(nameof(comicTextOverlayService));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _comicRepository = comicRepository ?? throw new ArgumentNullException(nameof(comicRepository));
        _leaderboardService = leaderboardService ?? throw new ArgumentNullException(nameof(leaderboardService));
        _contentSafetyScreener = contentSafetyScreener ?? throw new ArgumentNullException(nameof(contentSafetyScreener));
        _moderationGate = moderationGate ?? throw new ArgumentNullException(nameof(moderationGate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        _generationLock = generationLock ?? throw new ArgumentNullException(nameof(generationLock));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// A cached comic is usable when it has not expired <em>and</em> was drawn under the prompt
    /// version currently in force.
    /// <para>
    /// The second half is what makes changing a prompt cheap to evaluate. Without it a tune kept
    /// serving the previous prompt's output until somebody paid for a forced regeneration, so the
    /// only way to see the new behaviour was to spend on every restaurant being compared — and
    /// the easy mistake was to compare new output against an old comic and blame the prompt.
    /// A version mismatch falls through to generation on demand, which needs no migration and no
    /// bulk job, and a row written before versioning existed reads back as 0 and so misses once.
    /// </para>
    /// </summary>
    private bool IsCacheUsable(Comic? comic) =>
        comic is not null
        && comic.ExpiresAt > _timeProvider.GetUtcNow()
        && comic.PromptVersion == _options.PromptVersion;

    /// <summary>
    /// Generates or retrieves cached comic for a restaurant.
    /// </summary>
    /// <param name="placeId">Google Maps place ID</param>
    /// <param name="forceRegenerate">If true, regenerates even if valid cache exists</param>
    /// <param name="progress">Optional sink for the stage currently running; best-effort, never awaited</param>
    /// <param name="cancellationToken">Cancels the generation pipeline</param>
    /// <returns>Generated or cached Comic entity</returns>
    /// <exception cref="KeyNotFoundException">If restaurant not found</exception>
    /// <exception cref="InsufficientReviewsException">If restaurant has fewer than required reviews</exception>
    public async Task<Comic> GenerateComicAsync(
        PlaceId placeId,
        bool forceRegenerate = false,
        IProgress<ComicGenerationPhase>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (placeId.IsEmpty)
            throw new ArgumentException("PlaceId is required", nameof(placeId));

        _logger.GeneratingComic(placeId.Value, forceRegenerate);

        var overallStopwatch = Stopwatch.StartNew();

        // Check cache first (unless force regenerate)
        if (!forceRegenerate)
        {
            var cachedComic = await _comicRepository.GetByPlaceIdAsync(placeId);
            if (cachedComic is not null && IsCacheUsable(cachedComic))
            {
                _logger.ReturningCachedComic(placeId.Value);

                // Refresh SAS token if it is expired or within 2 hours of expiry
                if (SasUrl.IsExpiringSoon(cachedComic.ImageUrl, treatUnsignedAzureUrlAsStale: true))
                {
                    _logger.LogInformation("SAS token for cached comic {PlaceId} is expired/expiring — refreshing", placeId);
                    cachedComic.ImageUrl = await _blobStorageService.RefreshSasUrlAsync(cachedComic.ImageUrl);
                    await _comicRepository.UpsertAsync(cachedComic);
                }

                cachedComic.CacheState = ComicCacheState.Cached; // Mark provenance for the caller
                progress?.Report(ComicGenerationPhase.CacheHit);
                _telemetryClient.GetMetric("Comics.CacheHit").TrackValue(1);
                overallStopwatch.Stop();
                _telemetryClient.GetMetric("Comics.Generation.RequestDurationMs").TrackValue(overallStopwatch.Elapsed.TotalMilliseconds);
                return cachedComic;
            }
        }

        _telemetryClient.GetMetric("Comics.CacheMiss").TrackValue(1);

        // Single flight. The cache was read and missed a moment ago, and everything below spends
        // money. Two requests for one restaurant arriving inside that window — a double-tap on a
        // phone, two tabs — would both miss and both pay for the same comic, with the loser's
        // output discarded by the upsert. A per-place gate makes the second caller wait, re-read,
        // and find the first one's comic instead of commissioning its own.
        await using var generationGate = await _generationLock.AcquireAsync(placeId, cancellationToken);

        // Re-read under the gate: the wait may have been exactly as long as the other request's
        // generation, which is now in the cache.
        if (!forceRegenerate)
        {
            var raced = await _comicRepository.GetByPlaceIdAsync(placeId);
            if (raced is not null && IsCacheUsable(raced))
            {
                _logger.LogInformation(
                    "Comic for placeId {PlaceId} was generated while this request waited — serving it",
                    placeId.Value);
                _telemetryClient.GetMetric("Comics.Generation.Deduplicated").TrackValue(1);
                raced.CacheState = ComicCacheState.Cached;
                progress?.Report(ComicGenerationPhase.CacheHit);
                overallStopwatch.Stop();
                _telemetryClient.GetMetric("Comics.Generation.RequestDurationMs").TrackValue(overallStopwatch.Elapsed.TotalMilliseconds);
                return raced;
            }
        }

        // Fetch restaurant details with reviews
        progress?.Report(ComicGenerationPhase.FetchingReviews);
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
        progress?.Report(ComicGenerationPhase.AnalyzingStrangeness);
        var analysisStopwatch = Stopwatch.StartNew();
        var analysis = await _chatService.AnalyzeStrangenessAsync(reviewsForAnalysis, cancellationToken);
        analysisStopwatch.Stop();

        var strangenessScore = analysis.StrangenessScore;
        var panelCount = analysis.PanelCount;
        var narrative = analysis.Narrative;

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

        // Pre-publish content screen. Positioned here for two reasons: it is the first point at
        // which the model-authored prose exists, and it is still before the paid image call, so
        // a refusal costs nothing. The app publishes AI-written text about real, named businesses
        // and until now nothing looked at it between the model and the reader.
        var screen = await _contentSafetyScreener.ScreenAsync(narrative, cancellationToken);

        if (screen.IsBlocked)
        {
            _logger.LogWarning("Content screen blocked the narrative for placeId {PlaceId} ({Category})",
                placeId, screen.Category);
            _telemetryClient.GetMetric("Comics.ContentBlocked").TrackValue(1);
            throw new ContentBlockedException(screen.Category ?? "unknown");
        }

        if (screen.IsFlagged)
        {
            // Publishes, and lands in the moderation queue. Withholding on this signal alone
            // would refuse the app's best comics — allegation language is ordinary in the
            // one-star reviews the whole product mines.
            await _moderationGate.FlagForReviewAsync(placeId, screen.Category ?? "unknown", cancellationToken);
            _telemetryClient.GetMetric("Comics.ContentFlagged").TrackValue(1);
        }

        // Generate comic image (panel count capped at 2)
        progress?.Report(ComicGenerationPhase.GeneratingArtwork);
        var imageStopwatch = Stopwatch.StartNew();
        byte[] imageBytes;
        try
        {
            imageBytes = await _imageGenerationService.GenerateComicImageAsync(narrative, panelCount, cancellationToken);
        }
        catch (ImageDeclinedException ex)
        {
            // The image model refused to depict what the reviews describe. Nothing is published,
            // and the refusal lands in the moderation queue — which is the point: it is a signal
            // about the source material that nobody would otherwise ever see. This replaces a
            // fallback that answered a refusal by paying for a second image of an unrelated
            // cheerful restaurant and publishing it under these reviews' score.
            _telemetryClient.GetMetric("Comics.ImageDeclined").TrackValue(1);
            await _moderationGate.FlagForReviewAsync(placeId, "image_declined", cancellationToken);
            _logger.LogWarning("Image model declined to draw placeId {PlaceId}: {Reason}", placeId.Value, ex.Reason);
            throw;
        }
        imageStopwatch.Stop();

        _logger.LogInformation("Generated {PanelCount}-panel comic image: {Size} bytes", panelCount, imageBytes.Length);
        _telemetryClient.GetMetric("Comics.Generation.ImageDurationMs").TrackValue(imageStopwatch.Elapsed.TotalMilliseconds);

        // Add readable text caption overlays to each panel (replaces garbled AI-rendered text).
        // The captions arrive with the analysis: one completion produces the score, the narrative
        // and the panel text together, so this step no longer waits on a model of its own. It was
        // the second paid round trip in a pipeline that only ever needed one.
        progress?.Report(ComicGenerationPhase.ComposingStrip);
        var overlayStopwatch = Stopwatch.StartNew();
        var captions = ChatPrompts.NormalizeCaptions(analysis.Captions, narrative, panelCount);
        imageBytes = await _comicTextOverlayService.AddTextOverlayAsync(imageBytes, captions, panelCount, cancellationToken);
        overlayStopwatch.Stop();

        _logger.LogInformation("Added text overlay to comic: {Size} bytes", imageBytes.Length);
        _telemetryClient.GetMetric("Comics.Generation.TextOverlayDurationMs").TrackValue(overlayStopwatch.Elapsed.TotalMilliseconds);

        // The comic's own colours, read off the finished bytes while they are still in memory.
        // Has to happen here rather than on the client: the blob is served without CORS headers,
        // so a browser canvas that has drawn it cannot be read back.
        var palette = ComicPaletteExtractor.Extract(imageBytes);

        // The vector that will let another comic find this one. Best-effort by contract: an empty
        // array means "not a similarity candidate", which costs a related link and never a comic,
        // and it is what a switched-off or unreachable embedding backend returns.
        var embedding = await _embeddingService.EmbedAsync(narrative, cancellationToken);

        // Upload to blob storage
        progress?.Report(ComicGenerationPhase.Publishing);
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
            CacheState = ComicCacheState.Generated,
            Palette = palette,
            PromptVersion = _options.PromptVersion,
            Embedding = embedding
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
    /// The profanity filter, as one alternation. These keywords used to be matched in a loop of
    /// interpolated patterns: 16 keywords against a process-wide <see cref="Regex"/> cache that
    /// holds 15, so the LRU evicted on every pass — every keyword was re-parsed for every review,
    /// and other callers' patterns were evicted along with them. Generated at build time now.
    /// </summary>
    [GeneratedRegex(
        @"\b(?:fuck|shit|ass|bitch|bastard|piss|slut|whore|damn|crap|hell|dick|cock|penis|vagina|anus)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InappropriateContentRegex();

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

        return InappropriateContentRegex().IsMatch(normalized);
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
    /// Gets cached comic for a restaurant if it exists and hasn't expired
    /// </summary>
    public async Task<Comic?> GetCachedComicAsync(PlaceId placeId, CancellationToken cancellationToken = default)
    {
        if (placeId.IsEmpty)
            throw new ArgumentException("PlaceId is required", nameof(placeId));

        var cachedComic = await _comicRepository.GetByPlaceIdAsync(placeId);

        if (cachedComic is not null && IsCacheUsable(cachedComic))
        {
            _logger.LogInformation("Found valid cached comic for placeId: {PlaceId}", placeId);

            if (SasUrl.IsExpiringSoon(cachedComic.ImageUrl, treatUnsignedAzureUrlAsStale: true))
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
