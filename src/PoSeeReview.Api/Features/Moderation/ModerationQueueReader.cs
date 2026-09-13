using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Storage;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Enums;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Moderation;

/// <summary>
/// Builds the queue a moderator actually reads: reports, grouped by place, joined to whatever
/// state has already been recorded and to whether a live comic still exists.
/// <para>
/// Before this, <c>/api/reports</c> wrote rows that no interface ever read. A report intake with
/// no queue behind it is a form that says "thanks, we look at these daily" and then does not.
/// </para>
/// </summary>
public sealed class ModerationQueueReader(
    TableServiceClient tableServiceClient,
    ModerationRepository moderationRepository,
    IComicRepository comicRepository,
    IOptions<AzureStorageOptions> storageOptions,
    IOptions<ModerationOptions> moderationOptions,
    TimeProvider timeProvider)
{
    private readonly TableClient _reports =
        tableServiceClient.GetTableClient(storageOptions.Value.ReportsTableName);

    public async Task<ModerationQueueDto> GetQueueAsync(CancellationToken cancellationToken = default)
    {
        var options = moderationOptions.Value;
        var since = timeProvider.GetUtcNow().AddDays(-Math.Max(1, options.QueueWindowDays));

        var reportsByPlace = await ReadReportsAsync(since, options.QueueLimit, cancellationToken);
        var state = await moderationRepository.GetAllAsync(options.QueueLimit, cancellationToken);

        // Places with state but no recent reports still belong in the queue: a suppressed place
        // or one hidden by hand is exactly what a moderator comes back to check.
        var placeIds = reportsByPlace.Keys.Union(state.Keys, StringComparer.Ordinal).ToList();

        var items = new List<ModerationItemDto>(placeIds.Count);

        foreach (var placeId in placeIds)
        {
            reportsByPlace.TryGetValue(placeId, out var reports);
            state.TryGetValue(placeId, out var row);

            var comic = await comicRepository.GetByPlaceIdAsync(PlaceId.From(placeId));
            var hasLiveComic = comic is not null && comic.ExpiresAt > timeProvider.GetUtcNow();

            items.Add(new ModerationItemDto
            {
                PlaceId = placeId,
                RestaurantName = comic?.RestaurantName ?? string.Empty,
                ReportCount = reports?.Count ?? row?.ReportCount ?? 0,
                TopReason = TopReasonOf(reports),
                LastReportedAt = reports is { Count: > 0 }
                    ? reports.Max(r => r.ReportedAt)
                    : row?.UpdatedAt ?? default,
                IsHidden = row?.IsHidden ?? false,
                IsSuppressed = row?.IsSuppressed ?? false,
                IsReviewed = row?.IsReviewed ?? false,
                Reason = row?.Reason ?? string.Empty,
                HasLiveComic = hasLiveComic
            });
        }

        return new ModerationQueueDto
        {
            // Unreviewed first, then by weight of reports. A moderator opening this page should
            // land on the thing nobody has looked at yet, not on the busiest settled row.
            Items = [.. items
                .OrderBy(i => i.IsReviewed)
                .ThenByDescending(i => i.ReportCount)
                .ThenByDescending(i => i.LastReportedAt)],
            AutoHideThreshold = options.AutoHideReportThreshold
        };
    }

    private async Task<Dictionary<string, List<ModerationReportRow>>> ReadReportsAsync(
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = TableClient.CreateQueryFilter<ModerationReportRow>(e => e.ReportedAt >= since);
        var byPlace = new Dictionary<string, List<ModerationReportRow>>(StringComparer.Ordinal);
        var scanned = 0;

        await foreach (var row in _reports.QueryAsync<ModerationReportRow>(filter, cancellationToken: cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(row.PlaceId))
            {
                continue;
            }

            if (!byPlace.TryGetValue(row.PlaceId, out var list))
            {
                list = [];
                byPlace[row.PlaceId] = list;
            }

            list.Add(row);

            // Bounded on rows, not on places: a cross-partition scan on an operator page is a
            // self-inflicted timeout waiting to happen, and the same reasoning already caps
            // ComicReportRepository.GetRecentAsync.
            if (++scanned >= limit * 10)
            {
                break;
            }
        }

        return byPlace;
    }

    /// <summary>
    /// The most-cited reason, which is what tells a moderator what kind of problem this is
    /// before they open anything.
    /// </summary>
    private static ComicReportReason TopReasonOf(List<ModerationReportRow>? reports)
    {
        if (reports is null || reports.Count == 0)
        {
            return ComicReportReason.Other;
        }

        var top = reports
            .Select(r => Enum.TryParse<ComicReportReason>(r.Reason, out var parsed) ? parsed : ComicReportReason.Other)
            .GroupBy(reason => reason)
            .OrderByDescending(group => group.Count())
            .First();

        return top.Key;
    }
}
