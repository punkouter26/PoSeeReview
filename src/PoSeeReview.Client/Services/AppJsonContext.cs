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
[JsonSerializable(typeof(DevSessionDto))]
[JsonSerializable(typeof(MockStatusDto))]
[JsonSerializable(typeof(AuthStateDto))]
[JsonSerializable(typeof(ComicStatsDto))]
[JsonSerializable(typeof(GenerationBudgetDto))]
[JsonSerializable(typeof(ReactionCountsDto))]
[JsonSerializable(typeof(ReactionRequestDto))]
[JsonSerializable(typeof(ComicReportRequestDto))]
[JsonSerializable(typeof(ComicReportResponseDto))]
[JsonSerializable(typeof(HallOfFameResponse))]
[JsonSerializable(typeof(FunnelEventDto))]
[JsonSerializable(typeof(FunnelSnapshotDto))]
// Not a wire DTO: the locally-stored comic history. It lives here for the same reason the rest
// do — the client is trim-analyzed, so every type it serializes needs generated metadata.
[JsonSerializable(typeof(List<ComicHistoryEntry>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
