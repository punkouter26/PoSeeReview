namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// A short, shareable address for one comic.
/// </summary>
public class ShareLinkDto
{
    /// <summary>The opaque code, e.g. <c>k7Qm2xr</c>.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Absolute short URL — what the user actually shares.</summary>
    public string ShortUrl { get; set; } = string.Empty;

    /// <summary>Absolute URL of the comic page the short link resolves to.</summary>
    public string CanonicalUrl { get; set; } = string.Empty;
}
