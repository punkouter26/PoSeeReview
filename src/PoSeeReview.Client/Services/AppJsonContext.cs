using System.Text.Json.Serialization;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Client.Services;

/// <summary>
/// Source-generated System.Text.Json metadata for every DTO the client (de)serializes.
/// Keeps the trimmable WASM client free of reflection-based serialization (NET_RULES 6.6).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NearbyRestaurantsResponse))]
[JsonSerializable(typeof(ComicDto))]
[JsonSerializable(typeof(ComicGenerationEventDto))]
[JsonSerializable(typeof(LeaderboardResponse))]
[JsonSerializable(typeof(HealthStatusDto))]
[JsonSerializable(typeof(DiagnosticsSnapshotDto))]
[JsonSerializable(typeof(MockStatusDto))]
[JsonSerializable(typeof(AuthStateDto))]
[JsonSerializable(typeof(ComicStatsDto))]
[JsonSerializable(typeof(GenerationBudgetDto))]
[JsonSerializable(typeof(ComicReportRequestDto))]
[JsonSerializable(typeof(ComicReportResponseDto))]
[JsonSerializable(typeof(InsightsDto))]
[JsonSerializable(typeof(ShareLinkDto))]
[JsonSerializable(typeof(ModerationQueueDto))]
[JsonSerializable(typeof(ModerationActionDto))]
[JsonSerializable(typeof(CachedComicsResponse))]
// The comic's invented conversation, fetched on demand by the comic page's play button.
[JsonSerializable(typeof(ComicAudioSkit))]
// Not a wire DTO: the remembered leaderboard ranks (BoardMemoryService).
[JsonSerializable(typeof(Dictionary<string, int>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
