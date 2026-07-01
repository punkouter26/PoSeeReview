using Microsoft.Extensions.Logging;

namespace PoSeeReview.Infrastructure.Comics;

/// <summary>
/// Source-generated, allocation-free log messages for the comic generation hot path
/// (NET_RULES 6.1). High-frequency entry/cache/completion events live here so the
/// runtime avoids value boxing and template-string allocation.
/// </summary>
internal static partial class ComicGenerationLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Generating comic for placeId: {PlaceId}, forceRegenerate: {ForceRegenerate}")]
    public static partial void GeneratingComic(this ILogger logger, string placeId, bool forceRegenerate);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Returning cached comic for placeId: {PlaceId}")]
    public static partial void ReturningCachedComic(this ILogger logger, string placeId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Comic generation complete for placeId: {PlaceId}")]
    public static partial void ComicGenerationComplete(this ILogger logger, string placeId);
}
