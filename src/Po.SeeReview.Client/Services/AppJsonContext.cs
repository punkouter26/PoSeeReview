using System.Text.Json.Serialization;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Client.Services;

/// <summary>
/// Source-generated System.Text.Json metadata for every DTO the client (de)serializes.
/// Keeps the trimmable WASM client free of reflection-based serialization (NET_RULES 6.6).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NearbyRestaurantsResponse))]
[JsonSerializable(typeof(ComicDto))]
[JsonSerializable(typeof(LeaderboardResponse))]
[JsonSerializable(typeof(HealthStatusDto))]
[JsonSerializable(typeof(DiagnosticsSnapshotDto))]
[JsonSerializable(typeof(DevSessionDto))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
