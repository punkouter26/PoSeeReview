namespace PoSeeReview.Api.Middleware;

/// <summary>
/// Recognises the link-preview fetchers that unfurl a shared URL in a chat client or timeline.
/// Cross-cutting on purpose: <see cref="UserAgentValidationMiddleware"/> has to stop rejecting
/// them, and the Comics slice has to answer them with Open Graph tags, and neither should own
/// the list or reference the other.
/// </summary>
internal static class SocialCrawlers
{
    /// <summary>
    /// Substring match against the raw User-Agent. These bots do not run JavaScript, so a share
    /// of a Blazor WASM URL unfurls as a blank card unless the server answers them directly.
    /// </summary>
    private static readonly string[] KnownAgents =
    [
        "facebookexternalhit",
        "facebookcatalog",
        "Facebot",
        "Twitterbot",
        "Slackbot",
        "Slack-ImgProxy",
        "Discordbot",
        "WhatsApp",
        "LinkedInBot",
        "TelegramBot",
        "Applebot",
        "redditbot",
        "Iframely",
        "SkypeUriPreview",
        "Embedly",
        "Pinterest",
        "vkShare",
        "Mastodon",
        "Bluesky",
        "Googlebot",
        "bingbot",
        "DuckDuckBot"
    ];

    public static bool IsSocialCrawler(string? userAgent) =>
        !string.IsNullOrWhiteSpace(userAgent)
        && KnownAgents.Any(agent => userAgent.Contains(agent, StringComparison.OrdinalIgnoreCase));
}
