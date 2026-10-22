using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Shared.Dtos;

/// <summary>One place in the moderation queue: its reports and its current state.</summary>
public class ModerationItemDto
{
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>Restaurant name, when a live comic still exists to read it from.</summary>
    public string RestaurantName { get; set; } = string.Empty;

    /// <summary>How many distinct people reported it.</summary>
    public int ReportCount { get; set; }

    /// <summary>Most-cited reason across those reports.</summary>
    public ComicReportReason TopReason { get; set; } = ComicReportReason.Other;

    /// <summary>Most recent report.</summary>
    public DateTimeOffset LastReportedAt { get; set; }

    public bool IsHidden { get; set; }

    public bool IsSuppressed { get; set; }

    /// <summary>True once someone has acted on it, so a queue can hide settled rows.</summary>
    public bool IsReviewed { get; set; }

    /// <summary>Operator note attached by the last action.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>True while a live, unexpired comic exists for this place.</summary>
    public bool HasLiveComic { get; set; }

    /// <summary>The AI triage of the live comic, when a moderator has asked for one. Advice only.</summary>
    public ModerationTriageDto? Triage { get; set; }
}

/// <summary>
/// An AI second opinion on a queued comic: keep, hide or remove, how sure, and why. It never acts
/// on its own; a moderator reads it beside the comic and decides.
/// </summary>
public class ModerationTriageDto
{
    /// <summary><c>keep</c>, <c>hide</c> or <c>remove</c>.</summary>
    public string Suggestion { get; set; } = string.Empty;

    /// <summary>0-100.</summary>
    public int Confidence { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string[] Concerns { get; set; } = [];

    public DateTimeOffset TriagedAt { get; set; }
}

/// <summary>The queue, plus the thresholds it is being judged against.</summary>
public class ModerationQueueDto
{
    public List<ModerationItemDto> Items { get; set; } = [];

    /// <summary>Distinct reports that auto-hide a comic pending review.</summary>
    public int AutoHideThreshold { get; set; }
}

/// <summary>A moderator's decision about one place.</summary>
public class ModerationActionDto
{
    /// <summary>Free-text note, stored on the row and never shown to end users.</summary>
    public string Reason { get; set; } = string.Empty;
}
