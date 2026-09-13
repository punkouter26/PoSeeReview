using PoSeeReview.Api.Storage;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using PoSeeReview.Shared.Contracts;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>Composes the 1200x630 image a shared link unfurls as.</summary>
public interface IShareCardService
{
    /// <summary>
    /// Renders the card for a comic, or null when the comic's artwork is no longer retrievable.
    /// </summary>
    Task<byte[]?> RenderAsync(Comic comic, CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the link-preview card.
/// <para>
/// This exists because <c>og:image</c> used to point straight at the comic's blob URL, and that
/// URL carries a SAS signature that lapses after about a week while the Hall of Fame entry it
/// came from is designed to outlive everything. Every share older than the signature degraded
/// to a blank card. Serving the image from this origin makes the preview outlive the signature
/// and lets the card carry the score and the wordmark, which the raw comic does not.
/// </para>
/// <para>
/// Composed on demand rather than stored. It is a deterministic function of a comic that already
/// exists, the render is a resize and a handful of draw calls, and caching it would add a second
/// blob lifecycle that takedown would then have to know about.
/// </para>
/// </summary>
public sealed class ShareCardService(
    IBlobStorageService blobStorageService,
    ILogger<ShareCardService> logger) : IShareCardService
{
    /// <summary>The size every major link-preview client expects. Anything else gets letterboxed.</summary>
    public const int CardWidth = 1200;
    public const int CardHeight = 630;

    // Brand colours are literals here on purpose. This is a server-rendered PNG: there is no
    // stylesheet to read, no viewer theme to respect, and a card that changed appearance
    // between renders would be a worse artifact, not a more correct one. They mirror
    // --color-brand / --color-brand-dark / --color-accent in app.css.
    private static readonly Color BrandDark = Color.ParseHex("#2E1065");
    private static readonly Color Accent = Color.ParseHex("#F59E0B");
    private static readonly Color OnDark = Color.ParseHex("#F8FAFC");
    private static readonly Color OnDarkMuted = Color.ParseHex("#CBD5E1");

    /// <summary>Height of the bottom band that carries the name and wordmark.</summary>
    private const int ScrimHeight = 190;

    public async Task<byte[]?> RenderAsync(Comic comic, CancellationToken cancellationToken = default)
    {
        try
        {
            using var card = new Image<Rgba32>(CardWidth, CardHeight);
            card.Mutate(ctx => ctx.Fill(BrandDark));

            await DrawArtworkAsync(card, comic, cancellationToken);
            DrawScrim(card);
            DrawScoreBadge(card, comic.StrangenessScore);
            DrawCaption(card, comic.RestaurantName);

            using var output = new MemoryStream();
            await card.SaveAsPngAsync(output, cancellationToken);
            return output.ToArray();
        }
        catch (Exception ex)
        {
            // A preview is decoration. Failing to build one returns null so the caller can 404
            // and let the crawler fall back to the text tags, which is a worse card but not a
            // broken page.
            logger.LogWarning(ex, "Failed to render the share card for {PlaceId}", comic.PlaceId);
            return null;
        }
    }

    /// <summary>
    /// Draws the comic itself, cropped to fill the card.
    /// <para>
    /// A missing blob is not a failure: the comic may have been cleaned up while its Hall of
    /// Fame row lives on. The card still renders — brand ground, score, name — which is exactly
    /// the case the old blob-URL <c>og:image</c> could not survive.
    /// </para>
    /// </summary>
    private async Task DrawArtworkAsync(Image<Rgba32> card, Comic comic, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(comic.ImageUrl))
        {
            return;
        }

        await using var stream = await blobStorageService.OpenComicImageStreamAsync(comic.ImageUrl, cancellationToken);
        if (stream is null)
        {
            logger.LogInformation("Share card for {PlaceId} has no artwork; rendering the text-only card", comic.PlaceId);
            return;
        }

        using var artwork = await Image.LoadAsync<Rgba32>(stream, cancellationToken);

        artwork.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(CardWidth, CardHeight),
            // Crop rather than pad: a comic letterboxed onto brand purple reads as a mistake,
            // and the panels nearest the centre are the ones worth showing in a thumbnail.
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }));

        card.Mutate(ctx => ctx.DrawImage(artwork, 1f));
    }

    /// <summary>
    /// A darkening band across the bottom third so the caption is readable over any artwork.
    /// <para>
    /// Drawn as stacked one-pixel rows rather than a single translucent rectangle: a flat
    /// overlay has a hard top edge that reads as a bug on a light comic panel.
    /// </para>
    /// </summary>
    private static void DrawScrim(Image<Rgba32> card)
    {
        card.Mutate(ctx =>
        {
            for (var y = 0; y < ScrimHeight; y++)
            {
                var progress = y / (float)ScrimHeight;
                var alpha = (byte)(235 * progress * progress);
                var row = new RectangleF(0, CardHeight - ScrimHeight + y, CardWidth, 1);
                ctx.Fill(Color.FromRgba(15, 6, 35, alpha), row);
            }

            // A brand rule along the very bottom, so the card is recognisable at thumbnail size
            // even when the artwork is missing.
            ctx.Fill(Accent, new RectangleF(0, CardHeight - 8, CardWidth, 8));
        });
    }

    private static void DrawScoreBadge(Image<Rgba32> card, int score)
    {
        const int Diameter = 168;
        const int Margin = 40;

        var centre = new PointF(CardWidth - Margin - (Diameter / 2f), Margin + (Diameter / 2f));
        var circle = new SixLabors.ImageSharp.Drawing.EllipsePolygon(centre, Diameter / 2f);

        var scoreFont = ResolveFont(74, FontStyle.Bold);
        var labelFont = ResolveFont(20, FontStyle.Bold);

        card.Mutate(ctx =>
        {
            ctx.Fill(Color.FromRgba(15, 6, 35, 200), circle);
            ctx.Draw(Accent, 6f, circle);

            ctx.DrawText(new RichTextOptions(scoreFont)
            {
                Origin = new PointF(centre.X, centre.Y - 12),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, score.ToString(), Accent);

            ctx.DrawText(new RichTextOptions(labelFont)
            {
                Origin = new PointF(centre.X, centre.Y + 44),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, "/ 100", OnDarkMuted);
        });
    }

    private static void DrawCaption(Image<Rgba32> card, string restaurantName)
    {
        const int Margin = 48;

        var nameFont = ResolveFont(52, FontStyle.Bold);
        var markFont = ResolveFont(24, FontStyle.Bold);

        // Third-party text of unbounded length. Wrapped to the card, then hard-capped so a
        // pathological name cannot push the wordmark off the bottom edge.
        var name = restaurantName.Trim();
        if (name.Length > 70)
        {
            name = name[..69].TrimEnd() + "…";
        }

        card.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(markFont)
            {
                Origin = new PointF(Margin, CardHeight - Margin - 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom
            }, "POSEEREVIEW  ·  STRANGENESS SCORE", Accent);

            ctx.DrawText(new RichTextOptions(nameFont)
            {
                Origin = new PointF(Margin, CardHeight - Margin - 44),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                WrappingLength = CardWidth - (Margin * 2) - 200
            }, name, OnDark);
        });
    }

    /// <summary>
    /// The card's font family, resolved once per process.
    /// <para>
    /// <see cref="SystemFonts.Families"/> materialises every installed family on each access, so
    /// this must not run per render. The Comics slice already learned that the hard way with the
    /// per-panel caption font.
    /// </para>
    /// </summary>
    private static readonly Lazy<FontFamily> CardFontFamily = new(() =>
    {
        var families = SystemFonts.Families.ToList();
        if (families.Count == 0)
        {
            throw new InvalidOperationException("No system fonts are available to render a share card.");
        }

        // A neutral sans first — this is a wordmark and a headline, not a comic caption, so the
        // overlay service's preference for Comic Sans is the wrong one here.
        var preferred = families.FirstOrDefault(f =>
            f.Name.Contains("Segoe UI", StringComparison.OrdinalIgnoreCase)
            || f.Name.Contains("Arial", StringComparison.OrdinalIgnoreCase)
            || f.Name.Contains("DejaVu Sans", StringComparison.OrdinalIgnoreCase)
            || f.Name.Contains("Liberation Sans", StringComparison.OrdinalIgnoreCase));

        return preferred.Name is not null ? preferred : families[0];
    });

    private static Font ResolveFont(int size, FontStyle style) => CardFontFamily.Value.CreateFont(size, style);
}
