using PoSeeReview.Api.Storage;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using PoSeeReview.Shared.Contracts;

namespace PoSeeReview.Api.Features.Comics;

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
    ILogger<ShareCardService> logger)
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

    public Task<byte[]?> RenderAsync(Comic comic, CancellationToken cancellationToken = default) =>
        ComposeAsync([comic], "POSEEREVIEW  ·  STRANGENESS SCORE", cancellationToken);

    /// <summary>Two comics side by side, each with its own name and score, for a shared duel.</summary>
    public Task<byte[]?> RenderDuelAsync(Comic left, Comic right, CancellationToken cancellationToken = default) =>
        ComposeAsync([left, right], "POSEEREVIEW  ·  STRANGENESS DUEL", cancellationToken);

    /// <summary>
    /// One column per comic. The single card and the duel card are the same drawing at two
    /// widths, so they share every helper and only the scale differs.
    /// </summary>
    private async Task<byte[]?> ComposeAsync(Comic[] comics, string wordmark, CancellationToken cancellationToken)
    {
        try
        {
            using var card = new Image<Rgba32>(CardWidth, CardHeight);
            card.Mutate(ctx => ctx.Fill(BrandDark));

            var isDuel = comics.Length > 1;
            var scale = isDuel ? 0.75f : 1f;
            var columns = comics
                .Select((comic, i) => (Comic: comic, Area: new Rectangle(i * CardWidth / comics.Length, 0, CardWidth / comics.Length, CardHeight)))
                .ToList();

            foreach (var (comic, area) in columns)
            {
                await DrawArtworkAsync(card, comic, area, cancellationToken);
            }

            DrawScrim(card);

            foreach (var (comic, area) in columns)
            {
                DrawScoreBadge(card, comic.StrangenessScore, area, scale);
                DrawName(card, comic.RestaurantName, area, scale, isDuel);
            }

            if (isDuel)
            {
                DrawVersus(card);
            }

            DrawWordmark(card, wordmark);

            using var output = new MemoryStream();
            await card.SaveAsPngAsync(output, cancellationToken);
            return output.ToArray();
        }
        catch (Exception ex)
        {
            // A preview is decoration. Failing to build one returns null so the caller can 404
            // and let the crawler fall back to the text tags, which is a worse card but not a
            // broken page.
            logger.LogWarning(ex, "Failed to render the share card for {PlaceId}", comics[0].PlaceId);
            return null;
        }
    }

    /// <summary>
    /// Draws the comic itself, cropped to fill its column.
    /// <para>
    /// A missing blob is not a failure: the comic may have been cleaned up while its Hall of
    /// Fame row lives on. The card still renders — brand ground, score, name — which is exactly
    /// the case the old blob-URL <c>og:image</c> could not survive.
    /// </para>
    /// </summary>
    private async Task DrawArtworkAsync(Image<Rgba32> card, Comic comic, Rectangle area, CancellationToken cancellationToken)
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
            Size = area.Size,
            // Crop rather than pad: a comic letterboxed onto brand purple reads as a mistake,
            // and the panels nearest the centre are the ones worth showing in a thumbnail.
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }));

        card.Mutate(ctx => ctx.DrawImage(artwork, area.Location, 1f));
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

    private static void DrawScoreBadge(Image<Rgba32> card, int score, Rectangle area, float scale)
    {
        const int Margin = 40;
        var radius = 84 * scale;

        var centre = new PointF(area.Right - Margin - radius, Margin + radius);
        var circle = new SixLabors.ImageSharp.Drawing.EllipsePolygon(centre, radius);

        card.Mutate(ctx =>
        {
            ctx.Fill(Color.FromRgba(15, 6, 35, 200), circle);
            ctx.Draw(Accent, 6f * scale, circle);

            ctx.DrawText(Centred(ResolveFont(74 * scale), centre.X, centre.Y - (12 * scale)), score.ToString(), Accent);
            ctx.DrawText(Centred(ResolveFont(20 * scale), centre.X, centre.Y + (44 * scale)), "/ 100", OnDarkMuted);
        });
    }

    private static void DrawName(Image<Rgba32> card, string restaurantName, Rectangle area, float scale, bool isDuel)
    {
        // Third-party text of unbounded length. Wrapped to its column, then hard-capped so a
        // pathological name cannot push the wordmark off the bottom edge.
        var maxChars = isDuel ? 40 : 70;
        var name = restaurantName.Trim();
        if (name.Length > maxChars)
        {
            name = name[..(maxChars - 1)].TrimEnd() + "…";
        }

        card.Mutate(ctx => ctx.DrawText(new RichTextOptions(ResolveFont(52 * scale))
        {
            Origin = new PointF(area.Left + TextMargin, CardHeight - TextMargin - 44),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            WrappingLength = area.Width - (TextMargin * 2) - (isDuel ? 0 : 200)
        }, name, OnDark));
    }

    private static void DrawWordmark(Image<Rgba32> card, string wordmark) =>
        card.Mutate(ctx => ctx.DrawText(new RichTextOptions(ResolveFont(24))
        {
            Origin = new PointF(TextMargin, CardHeight - TextMargin - 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom
        }, wordmark, Accent));

    /// <summary>The seam between the two columns of a duel card, and the "VS" that names it.</summary>
    private static void DrawVersus(Image<Rgba32> card)
    {
        var centre = new PointF(CardWidth / 2f, (CardHeight - ScrimHeight) / 2f);
        var disc = new SixLabors.ImageSharp.Drawing.EllipsePolygon(centre, 52);

        card.Mutate(ctx =>
        {
            ctx.Fill(Accent, new RectangleF(centre.X - 3, 0, 6, CardHeight));
            ctx.Fill(BrandDark, disc);
            ctx.Draw(Accent, 6f, disc);
            ctx.DrawText(Centred(ResolveFont(44), centre.X, centre.Y), "VS", OnDark);
        });
    }

    private static RichTextOptions Centred(Font font, float x, float y) => new(font)
    {
        Origin = new PointF(x, y),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private const int TextMargin = 48;

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

    private static Font ResolveFont(float size) => CardFontFamily.Value.CreateFont(size, FontStyle.Bold);
}
