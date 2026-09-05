using System.Net.Http.Json;
using System.Text.Json;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Client.Services;

/// <summary>
/// HTTP client for calling the PoSeeReview API backend.
/// </summary>
public class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly DevSessionClient _devSessionClient;

    public ApiClient(HttpClient httpClient, DevSessionClient devSessionClient)
    {
        _httpClient = httpClient;
        _devSessionClient = devSessionClient;
    }

    /// <summary>
    /// Gets nearby restaurants based on coordinates.
    /// On non-success the response body is parsed for an RFC 7807 <c>ProblemDetails</c> payload
    /// so the user-facing message is actionable instead of a generic 503.
    /// Returns <c>null</c> only when the body cannot be parsed.
    /// </summary>
    public async Task<NearbyRestaurantsResponse?> GetNearbyRestaurantsAsync(
        double latitude,
        double longitude,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            $"/api/restaurants/nearby?latitude={latitude}&longitude={longitude}&limit={limit}");
        using var httpResponse = await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(httpResponse, "Nearby restaurant search failed", cancellationToken);

        return await httpResponse.Content.ReadFromJsonAsync(AppJsonContext.Default.NearbyRestaurantsResponse, cancellationToken);
    }

    /// <summary>
    /// Generates a comic for the given restaurant place ID.
    /// This may take 8-10 seconds for a new comic generation.
    /// On non-success the response body is parsed for an RFC 7807 <c>ProblemDetails</c> payload
    /// so the user-facing message is actionable instead of the opaque
    /// "net_http_message_not_success_statuscode_reason, 500, Internal Server Error" surfaced by
    /// <c>HttpRequestException</c>.
    /// </summary>
    public async Task<ComicDto> GenerateComicAsync(
        string placeId,
        bool forceRegenerate = false,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/comics/{placeId}";
        if (forceRegenerate)
        {
            url += "?forceRegenerate=true";
        }

        using var request = await CreateRequestAsync(HttpMethod.Post, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, "Comic generation failed", cancellationToken, "Please try again in a moment.");

        var comic = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.ComicDto, cancellationToken);
        return comic ?? throw new InvalidOperationException("Comic response was null");
    }

    /// <summary>
    /// Generates a comic while reporting the pipeline stage the server is genuinely in.
    /// <para>
    /// Falls back to <see cref="GenerateComicAsync"/> only when the stream is refused before any
    /// work could start — a non-success status on the request itself. Once the server has
    /// answered 200 the paid pipeline is running, so a mid-stream failure is surfaced as an
    /// error rather than retried: a retry there would pay for the same comic twice.
    /// </para>
    /// </summary>
    public async Task<ComicDto> GenerateComicStreamAsync(
        string placeId,
        bool forceRegenerate,
        IProgress<ComicGenerationPhase> progress,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/comics/{placeId}/stream";
        if (forceRegenerate)
        {
            url += "?forceRegenerate=true";
        }

        using var request = await CreateRequestAsync(HttpMethod.Post, url);
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // 404 means this build of the API predates streaming; 405 that it is routed
            // differently. Either way nothing was generated, so the plain POST is safe.
            if (response.StatusCode is System.Net.HttpStatusCode.NotFound
                or System.Net.HttpStatusCode.MethodNotAllowed)
            {
                return await GenerateComicAsync(placeId, forceRegenerate, cancellationToken);
            }

            await EnsureSuccessAsync(response, "Comic generation failed", cancellationToken, "Please try again in a moment.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            // Blank lines are SSE frame separators; anything that is not a data line is a
            // comment or a field this client does not use.
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line["data:".Length..].Trim();
            if (payload.Length == 0)
            {
                continue;
            }

            var evt = JsonSerializer.Deserialize(payload, AppJsonContext.Default.ComicGenerationEventDto);
            if (evt is null)
            {
                continue;
            }

            switch (evt.Kind)
            {
                case ComicGenerationEventDto.PhaseKind:
                    progress.Report(evt.Phase);
                    break;

                case ComicGenerationEventDto.CompleteKind when evt.Comic is not null:
                    return evt.Comic;

                case ComicGenerationEventDto.ErrorKind:
                    throw new HttpRequestException(
                        evt.ErrorDetail ?? evt.ErrorTitle ?? "Comic generation failed.",
                        null,
                        (System.Net.HttpStatusCode)evt.ErrorStatus);
            }
        }

        // A 200 that ends without a terminal event means the connection dropped mid-pipeline.
        throw new HttpRequestException(
            "The connection dropped while the comic was being drawn. Please try again.",
            null,
            System.Net.HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Fetches the cached comic for a place without triggering (paid) generation.
    /// Returns <c>null</c> when no valid cached comic exists (HTTP 404).
    /// </summary>
    public async Task<ComicDto?> GetCachedComicAsync(
        string placeId,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, $"/api/comics/{placeId}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "Comic lookup failed", cancellationToken);

        return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.ComicDto, cancellationToken);
    }

    /// <summary>
    /// Searches for restaurants by location query (city name or ZIP code).
    /// </summary>
    public async Task<NearbyRestaurantsResponse?> SearchRestaurantsByLocationAsync(
        string locationQuery,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/restaurants/search?location={Uri.EscapeDataString(locationQuery)}&limit={limit}";
        using var request = await CreateRequestAsync(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response, "Search request failed", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.NearbyRestaurantsResponse, cancellationToken);
        return payload;
    }

    /// <summary>
    /// Throws an <see cref="HttpRequestException"/> carrying the API's ProblemDetails
    /// <c>detail</c> when the response is not a success.
    /// <para>
    /// Every call site used to inline this: read body, <see cref="TryExtractProblemDetail"/>,
    /// throw. The copies had already drifted apart in wording, and two endpoints skipped it
    /// entirely for <c>EnsureSuccessStatusCode()</c> — which surfaces the framework's opaque
    /// "net_http_message_not_success_statuscode_reason" text that this helper exists to replace.
    /// </para>
    /// </summary>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken,
        string? suffix = null)
    {
        if (response.IsSuccessStatusCode)
            return;

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryExtractProblemDetail(errorBody)
            ?? $"{fallback} (HTTP {(int)response.StatusCode}).{(suffix is null ? "" : " " + suffix)}";

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string? TryExtractProblemDetail(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("detail", out var detailElement))
            {
                var detail = detailElement.GetString();
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    return detail;
                }
            }

            if (root.TryGetProperty("title", out var titleElement))
            {
                var title = titleElement.GetString();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title;
                }
            }
        }
        catch (JsonException)
        {
            // Response may not be JSON; fall through to null.
        }

        return null;
    }

    /// <summary>
    /// Gets the leaderboard entries for a given region.
    /// </summary>
    public async Task<LeaderboardResponse?> GetLeaderboardAsync(
        string region = "US",
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(
                HttpMethod.Get,
                $"/api/leaderboard?region={region}&limit={limit}");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "Leaderboard request failed", cancellationToken);
            return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.LeaderboardResponse, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the permanent weekly archive. Separate from the live board because these entries
    /// outlive the comics they came from.
    /// </summary>
    public async Task<HallOfFameResponse?> GetWeeklyHallOfFameAsync(
        string region = "US",
        int weeks = 4,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(
                HttpMethod.Get,
                $"/api/leaderboard/weekly?region={region}&weeks={weeks}&limit={limit}");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "Weekly archive request failed", cancellationToken);
            return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.HallOfFameResponse, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Regional context for a comic's score. Returns <c>null</c> when the comic has no stats
    /// yet — the score still renders, just without the comparison.
    /// </summary>
    public async Task<ComicStatsDto?> GetComicStatsAsync(
        string placeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, $"/api/comics/{placeId}/stats");
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync(AppJsonContext.Default.ComicStatsDto, cancellationToken)
                : null;
        }
        catch (HttpRequestException)
        {
            // Decoration on the payoff screen. It never justifies an error state.
            return null;
        }
    }

    /// <summary>
    /// What the signed-in user has left to spend today. Returns <c>null</c> when the budget
    /// cannot be read, which callers treat as "allow" — a failed read must not lock the app's
    /// primary action.
    /// </summary>
    public async Task<GenerationBudgetDto?> GetGenerationBudgetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, "/api/comics/budget");
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync(AppJsonContext.Default.GenerationBudgetDto, cancellationToken)
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Reads reaction tallies for a comic, plus the caller's own reaction.</summary>
    public async Task<ReactionCountsDto?> GetReactionsAsync(
        string placeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, $"/api/reactions/{placeId}");
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync(AppJsonContext.Default.ReactionCountsDto, cancellationToken)
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sets, changes or withdraws the caller's reaction. Passing <c>null</c> — or the reaction
    /// they already hold — withdraws it.
    /// </summary>
    public async Task<ReactionCountsDto?> SetReactionAsync(
        string placeId,
        ReactionKind? reaction,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Post, $"/api/reactions/{placeId}");
            request.Content = JsonContent.Create(
                new ReactionRequestDto { Reaction = reaction }, AppJsonContext.Default.ReactionRequestDto);

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync(AppJsonContext.Default.ReactionCountsDto, cancellationToken)
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Submits a viewer report. Unlike most calls here this one surfaces its failure: someone
    /// reporting content needs to know whether it was actually received.
    /// </summary>
    public async Task<ComicReportResponseDto> SubmitReportAsync(
        ComicReportRequestDto report,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "/api/reports");
        request.Content = JsonContent.Create(report, AppJsonContext.Default.ComicReportRequestDto);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Report submission failed", cancellationToken, "Please try again in a moment.");

        var payload = await response.Content.ReadFromJsonAsync(AppJsonContext.Default.ComicReportResponseDto, cancellationToken);
        return payload ?? throw new InvalidOperationException("Report response was null");
    }

    /// <summary>
    /// Reports one funnel step. Fire-and-forget by contract: it never throws and never blocks
    /// anything a user is waiting on.
    /// </summary>
    public async Task RecordFunnelEventAsync(
        string step,
        int? durationMs = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Post, "/api/analytics/events");
            request.Content = JsonContent.Create(
                new FunnelEventDto { Step = step, DurationMs = durationMs }, AppJsonContext.Default.FunnelEventDto);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            _ = response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            // Telemetry must never be able to break the thing it is measuring.
        }
    }

    /// <summary>Reads a day of funnel counters for the Diagnostics page.</summary>
    public async Task<FunnelSnapshotDto?> GetFunnelSnapshotAsync(
        int daysAgo = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, $"/api/analytics/funnel?daysAgo={daysAgo}");
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync(AppJsonContext.Default.FunnelSnapshotDto, cancellationToken)
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<HealthStatusDto?> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "/health");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Health request failed", cancellationToken);
        return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.HealthStatusDto, cancellationToken);
    }

    public async Task<DiagnosticsSnapshotDto?> GetDiagnosticsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "/diag");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Diagnostics request failed", cancellationToken);
        return await response.Content.ReadFromJsonAsync(AppJsonContext.Default.DiagnosticsSnapshotDto, cancellationToken);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        await _devSessionClient.AttachStoredHeaderAsync(request);
        return request;
    }
}
