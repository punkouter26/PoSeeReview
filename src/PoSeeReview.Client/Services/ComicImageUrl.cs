namespace PoSeeReview.Client.Services;

/// <summary>
/// Resolves the URL an <c>&lt;img&gt;</c> should actually load a comic image from.
/// <para>
/// A comic's URL is a SAS link straight to Blob Storage, and that is right in production: it
/// keeps every view on the storage account's bandwidth instead of the app's. Locally the same
/// link is <c>http://127.0.0.1:10000</c> while the app is served over HTTPS, and a browser will
/// not upgrade a mixed-content request whose host is an IP literal — so the Azurite URL is
/// blocked outright and the comic renders as a broken image with nothing but a console warning
/// to show for it. Three of the four call sites drew from that same URL.
/// </para>
/// <para>
/// Rewriting only when the URL is <em>insecure</em> keeps the production path byte-identical: an
/// https blob URL is returned untouched and never touches the app's bandwidth. The same-origin
/// image endpoint is used purely as the local stand-in, and it has the useful side effect of
/// making the strip CORS-readable locally, so the print post-process runs there too.
/// </para>
/// </summary>
public static class ComicImageUrl
{
    /// <param name="blobUrl">The SAS URL the API handed out. Empty once the blob has aged out.</param>
    /// <param name="placeId">Builds the same-origin fallback path; ignored when not needed.</param>
    public static string For(string? blobUrl, string? placeId)
    {
        if (string.IsNullOrWhiteSpace(blobUrl))
        {
            return string.Empty;
        }

        // Only http:// is rewritten. A production SAS URL is always https, so this branch is
        // unreachable in Azure and the proxy is never used to display a comic there.
        if (blobUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(placeId))
        {
            return $"/api/comics/{Uri.EscapeDataString(placeId)}/image";
        }

        return blobUrl;
    }
}
