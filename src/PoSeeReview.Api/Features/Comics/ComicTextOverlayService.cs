using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Draws readable captions onto a generated comic.
/// <para>
/// Image models render lettering as decoration — shapes that look like words — so every caption
/// this app ships is drawn here with a real font, over the top of whatever the model produced.
/// This service no longer talks to a model at all: captions arrive as an argument, which removed
/// a paid call from the critical path after the image already existed.
/// </para>
/// </summary>
public class ComicTextOverlayService : IComicTextOverlayService
{
    private readonly ILogger<ComicTextOverlayService> _logger;

    public ComicTextOverlayService(ILogger<ComicTextOverlayService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Adds per-panel English caption overlays. Each panel gets its own caption box at the top,
    /// covering any garbled AI-rendered text.
    /// </summary>
    public Task<byte[]> AddTextOverlayAsync(byte[] imageBytes, IReadOnlyList<string> captions, int panelCount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captions);

        if (imageBytes == null || imageBytes.Length == 0)
            throw new ArgumentException("Image bytes cannot be empty", nameof(imageBytes));

        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Adding per-panel text overlay to {PanelCount}-panel comic", panelCount);

        try
        {
            using var image = Image.Load<Rgba32>(imageBytes);
            var panelBounds = GetPanelBounds(image.Width, image.Height, panelCount);
            var drawn = 0;

            for (int i = 0; i < Math.Min(captions.Count, panelBounds.Count); i++)
            {
                if (string.IsNullOrWhiteSpace(captions[i]))
                    continue;

                // Scale the caption font to the panel width so text stays legible whether the
                // image is a small 512px square or a large 4-panel grid — a fixed size rendered
                // captions nearly invisible on full-resolution output.
                var fontSize = Math.Clamp((int)(panelBounds[i].Width / 20f), 16, 48);
                DrawPanelCaption(image, captions[i], panelBounds[i], GetComicFont(fontSize));
                drawn++;
            }

            using var outputStream = new MemoryStream();
            image.SaveAsPng(outputStream);

            _logger.LogInformation("Drew {Count} panel caption(s) onto comic", drawn);
            return Task.FromResult(outputStream.ToArray());
        }
        catch (Exception ex)
        {
            // Losing the captions must not lose the comic: the artwork is the expensive part and
            // an unlabelled strip is still the thing the user waited for.
            _logger.LogError(ex, "Failed to add text overlay, returning original image");
            return Task.FromResult(imageBytes);
        }
    }

    /// <summary>
    /// Returns approximate bounding rectangle for each panel.
    /// Matches the layouts requested by GeminiComicService prompts.
    /// </summary>
    private static List<RectangleF> GetPanelBounds(int width, int height, int panelCount)
    {
        const int Gutter = 6;
        var bounds = new List<RectangleF>();

        switch (panelCount)
        {
            case 1:
                bounds.Add(new RectangleF(0, 0, width, height));
                break;
            case 2:
                // Two panels stacked vertically
                var h2 = (height - Gutter) / 2f;
                bounds.Add(new RectangleF(0, 0, width, h2));
                bounds.Add(new RectangleF(0, h2 + Gutter, width, h2));
                break;
            case 3:
                // Three panels side-by-side horizontally
                var w3 = (width - 2 * Gutter) / 3f;
                bounds.Add(new RectangleF(0, 0, w3, height));
                bounds.Add(new RectangleF(w3 + Gutter, 0, w3, height));
                bounds.Add(new RectangleF(2 * (w3 + Gutter), 0, w3, height));
                break;
            default:
                // Four panels in 2x2 grid
                var w4 = (width - Gutter) / 2f;
                var h4 = (height - Gutter) / 2f;
                bounds.Add(new RectangleF(0, 0, w4, h4));
                bounds.Add(new RectangleF(w4 + Gutter, 0, w4, h4));
                bounds.Add(new RectangleF(0, h4 + Gutter, w4, h4));
                bounds.Add(new RectangleF(w4 + Gutter, h4 + Gutter, w4, h4));
                break;
        }

        return bounds;
    }

    /// <summary>
    /// Draws a classic comic-style yellow caption box at the top of the panel.
    /// Covers any garbled AI-rendered text while providing a clean, readable overlay.
    /// </summary>
    private void DrawPanelCaption(Image<Rgba32> image, string text, RectangleF panelBounds, Font font)
    {
        const int Padding = 6;
        var maxWrappingWidth = panelBounds.Width - Padding * 2 - 8;
        var origin = new PointF(
            panelBounds.X + panelBounds.Width / 2f,
            panelBounds.Y + Padding);

        var textOptions = new RichTextOptions(font)
        {
            Origin = origin,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            WrappingLength = maxWrappingWidth
        };

        var textBounds = TextMeasurer.MeasureBounds(text, textOptions);
        var bgRect = new RectangleF(
            textBounds.X - Padding,
            textBounds.Y - Padding,
            textBounds.Width + Padding * 2,
            textBounds.Height + Padding * 2);

        image.Mutate(ctx =>
        {
            // Classic comic yellow caption box
            ctx.Fill(new Color(new Rgba32(255, 255, 180, 245)), bgRect);
            ctx.Draw(Color.Black, 1.5f, bgRect);
            ctx.DrawText(textOptions, text, Color.Black);
        });

        _logger.LogDebug("Drew panel caption at ({X},{Y}): {Text}", panelBounds.X, panelBounds.Y, text);
    }

    /// <summary>
    /// The caption font family, resolved once for the process. Only the size varies per panel,
    /// and <see cref="SystemFonts.Families"/> materialises every installed family on each access —
    /// that whole scan used to run once per panel, up to four times per comic.
    /// </summary>
    private static readonly Lazy<FontFamily> ComicFontFamily = new(() =>
    {
        var families = SystemFonts.Families.ToList();
        if (families.Count == 0)
            throw new InvalidOperationException("No system fonts available, text overlay will be skipped");

        // Prefer Comic Sans MS, Arial, DejaVu or Liberation; otherwise take whatever is installed.
        var fontFamily = families.FirstOrDefault(f =>
            f.Name.Contains("Comic", StringComparison.OrdinalIgnoreCase) ||
            f.Name.Contains("Arial", StringComparison.OrdinalIgnoreCase) ||
            f.Name.Contains("DejaVu", StringComparison.OrdinalIgnoreCase) ||
            f.Name.Contains("Liberation", StringComparison.OrdinalIgnoreCase));

        return fontFamily.Name != null ? fontFamily : families[0];
    });

    /// <summary>
    /// Gets a comic-style font for text rendering
    /// </summary>
    private Font GetComicFont(int size)
    {
        try
        {
            return ComicFontFamily.Value.CreateFont(size, FontStyle.Bold);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get font, will skip text overlay");
            throw;
        }
    }
}
