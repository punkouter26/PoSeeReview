using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Pulls three dominant colours out of a finished comic so the client can tint itself to the
/// artwork it is showing.
/// <para>
/// This runs on the SERVER on purpose, and it is the only place it can run. The client cannot do
/// it: the comic blob is served without CORS headers, so a canvas that has drawn it is tainted
/// and <c>getImageData</c> throws a SecurityError — the same reason <c>comic-fx.js</c> usually
/// fails to attach. Extracting here costs one pass over a 48px thumbnail of an image that is
/// already decoded in memory, and ships three hex strings.
/// </para>
/// <para>
/// Deliberately not a quantizer. An octree or k-means pass would be more "correct" and would
/// reliably return three nearly identical browns for the average comic, because population alone
/// is dominated by paper and gutters. What the backdrop needs is the colours a person would name
/// if asked what this comic looks like, so buckets are ranked by population weighted by chroma,
/// and the colours that survive have to differ from each other in hue.
/// </para>
/// </summary>
internal static class ComicPaletteExtractor
{
    /// <summary>Colours returned. Matches the gradient shader's three colour uniforms.</summary>
    public const int PaletteSize = 3;

    /// <summary>
    /// Thumbnail edge used for sampling. 48px is ~2300 pixels: enough that a small but saturated
    /// speech bubble still lands in a bucket, small enough that the whole pass is immaterial next
    /// to the image generation that just happened.
    /// </summary>
    private const int SampleEdge = 48;

    /// <summary>
    /// Bits kept per channel when bucketing. 4 gives 4096 cells, which at 2300 samples is coarse
    /// enough to cluster neighbouring shades and fine enough to keep distinct hues apart.
    /// </summary>
    private const int BucketBits = 4;

    /// <summary>Minimum hue separation, in degrees, between two colours in the result.</summary>
    private const double MinHueSeparation = 25;

    /// <summary>
    /// Extracts the palette, or an empty array if the bytes are not a readable image.
    /// <para>
    /// Never throws. A missing palette is a comic that renders with the brand gradient, which is
    /// exactly what every comic did before this existed — so a decode failure here must not be
    /// allowed to fail a generation that has already been paid for.
    /// </para>
    /// </summary>
    public static string[] Extract(byte[] imageBytes)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            return [];
        }

        try
        {
            using var image = Image.Load<Rgba32>(imageBytes);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(SampleEdge, SampleEdge),
                Mode = ResizeMode.Stretch,
                // Box averages the block it collapses, which is what makes this a sample of the
                // whole image rather than of whichever pixels a nearest-neighbour pick landed on.
                Sampler = KnownResamplers.Box
            }));

            return Rank(Histogram(image));
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Running total for one histogram cell.</summary>
    private sealed class Cell
    {
        public double Weight;
        public long R;
        public long G;
        public long B;
        public int Count;
    }

    private static Dictionary<int, Cell> Histogram(Image<Rgba32> image)
    {
        var cells = new Dictionary<int, Cell>(256);
        const int Shift = 8 - BucketBits;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    if (pixel.A < 128)
                    {
                        continue;
                    }

                    var (_, saturation, lightness) = ToHsl(pixel.R, pixel.G, pixel.B);

                    // Paper and ink are the two most populous colours in every comic, and neither
                    // is one anybody would use to describe it. Dropping the extremes is what makes
                    // the remaining ranking about the artwork.
                    if (lightness is < 0.10 or > 0.94)
                    {
                        continue;
                    }

                    var key = ((pixel.R >> Shift) << (BucketBits * 2))
                            | ((pixel.G >> Shift) << BucketBits)
                            | (pixel.B >> Shift);

                    if (!cells.TryGetValue(key, out var cell))
                    {
                        cell = new Cell();
                        cells[key] = cell;
                    }

                    cell.R += pixel.R;
                    cell.G += pixel.G;
                    cell.B += pixel.B;
                    cell.Count++;

                    // Chroma weighting, with a floor so a genuinely monochrome comic still
                    // returns something rather than nothing. Squared because the gap between a
                    // grey-brown and an actual colour is exactly what this needs to exaggerate.
                    cell.Weight += 0.15 + (saturation * saturation);
                }
            }
        });

        return cells;
    }

    private static string[] Rank(Dictionary<int, Cell> cells)
    {
        if (cells.Count == 0)
        {
            return [];
        }

        var candidates = cells.Values
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Weight)
            .Take(64)
            .Select(c => (
                R: (int)(c.R / c.Count),
                G: (int)(c.G / c.Count),
                B: (int)(c.B / c.Count)))
            .ToList();

        var picked = new List<(int R, int G, int B)>(PaletteSize);

        foreach (var candidate in candidates)
        {
            if (picked.Count == PaletteSize)
            {
                break;
            }

            var (hue, _, _) = ToHsl(candidate.R, candidate.G, candidate.B);

            // Three shades of one orange is not a palette — it is a single colour with a
            // lighting gradient, and feeding it to a three-stop shader produces a flat wash.
            var tooClose = picked.Exists(p =>
            {
                var (otherHue, _, _) = ToHsl(p.R, p.G, p.B);
                var delta = Math.Abs(hue - otherHue);
                return Math.Min(delta, 360 - delta) < MinHueSeparation;
            });

            if (!tooClose)
            {
                picked.Add(candidate);
            }
        }

        // A comic that really is one hue still gets a full palette, by relaxing the separation
        // rule rather than by returning a short array every caller would have to special-case.
        foreach (var candidate in candidates)
        {
            if (picked.Count == PaletteSize)
            {
                break;
            }

            picked.Add(candidate);
        }

        return picked.Count < PaletteSize
            ? []
            : [.. picked.Select(p => $"#{p.R:x2}{p.G:x2}{p.B:x2}")];
    }

    /// <summary>
    /// Hue in degrees, saturation and lightness in 0..1. Hand-rolled because ImageSharp's
    /// colourspace converters would pull a whole conversion pipeline in for three lines of
    /// arithmetic run over 2300 pixels.
    /// </summary>
    private static (double Hue, double Saturation, double Lightness) ToHsl(int r, int g, int b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var lightness = (max + min) / 2.0;
        var delta = max - min;

        if (delta < 0.0001)
        {
            return (0, 0, lightness);
        }

        var saturation = lightness > 0.5
            ? delta / (2.0 - max - min)
            : delta / (max + min);

        double hue;
        if (Math.Abs(max - rd) < double.Epsilon)
        {
            hue = (((gd - bd) / delta) + (gd < bd ? 6 : 0)) * 60;
        }
        else if (Math.Abs(max - gd) < double.Epsilon)
        {
            hue = (((bd - rd) / delta) + 2) * 60;
        }
        else
        {
            hue = (((rd - gd) / delta) + 4) * 60;
        }

        return (hue, saturation, lightness);
    }

    /// <summary>
    /// Joins a palette for the single Table Storage column that persists it, and splits it back.
    /// One column rather than three: Table Storage is schemaless per row, so an older comic
    /// simply has no column here and reads back as an empty palette.
    /// </summary>
    public static string Join(string[] palette) => palette.Length == 0 ? string.Empty : string.Join(',', palette);

    /// <inheritdoc cref="Join"/>
    public static string[] Split(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return [];
        }

        var parts = stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == PaletteSize ? parts : [];
    }
}
