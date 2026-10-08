namespace PoSeeReview.Client.Services;

public static class RelativeTime
{
    /// <summary>"3h ago" — more useful in a list than a timestamp.</summary>
    public static string Format(DateTimeOffset when)
    {
        if (when == default)
        {
            return "—";
        }

        var elapsed = DateTimeOffset.UtcNow - when;

        return elapsed switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)elapsed.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"{(int)elapsed.TotalHours}h ago",
            { TotalDays: < 7 } => $"{(int)elapsed.TotalDays}d ago",
            _ => when.ToLocalTime().ToString("MMM d")
        };
    }
}
