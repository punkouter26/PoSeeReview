using System.Text.RegularExpressions;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// The positive-only image prompt shared by the painters that have no negative-prompt channel
/// (Gemini, Azure gpt-image). FLUX keeps its own because it does have one.
/// <para>
/// <b>The panel breakdown is the chat model's shot list, not a template.</b> It used to be a
/// fixed "1. Setup / 2. Punchline" under a one-paragraph narrative, which left the painter to
/// invent what each panel showed while the captions were written from the reviews, so the two
/// could disagree outright. Each numbered line is now the scene its caption describes.
/// </para>
/// </summary>
internal static partial class ComicImagePrompt
{
    /// <summary>
    /// Terms the image safety filters reject. The analysis prompt already asks for scenes in
    /// cartoon terms, so this is the backstop, not the plan: every hit is reported by the caller.
    /// </summary>
    [GeneratedRegex(
        @"\b(?:blood|bloody|kill|murder|dead|death|die|dying|gun|shoot|weapon|knife|stab|fight|attack|drug|cocaine|heroin|meth|naked|nude|sex|sexual|hate|racist|racial|vomit|puke|disgusting|roach|cockroach|rat|mice|vermin|poison|toxic|contaminated)\w*\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FlaggedTermRegex();

    /// <summary>Builds the prompt, blunting flagged terms, and reports which ones it blunted.</summary>
    public static (string Prompt, List<string> BluntedTerms) Build(string narrative, IReadOnlyList<string> scenes)
    {
        var blunted = new List<string>();
        var safeNarrative = Blunt(narrative, blunted);
        var breakdown = string.Join("\n", scenes.Select((scene, i) => $"{i + 1}. {Blunt(scene, blunted)}"));
        var panelCount = scenes.Count;

        var panelLayout = panelCount switch
        {
            1 => "Single-panorama comic strip (one wide scene filling the frame)",
            2 => "Two-panel comic strip with equal landscape panels stacked vertically",
            3 => "Three-panel strip with cinematic flow (left-to-right storytelling)",
            _ => "Four-panel comic strip arranged left-to-right, top-to-bottom (1-2 on top row, 3-4 on bottom row)"
        };

        // No negative-prompt channel: forbidden concepts named in the prompt ("NO SPEECH
        // BUBBLES") tend to get PAINTED INTO the artwork as literal lettering. Describe only what
        // we want — wordless, pantomime, blank surfaces — and never mention text, bubbles, or
        // writing. Captions are added later by the overlay service.
        var prompt = $"""
Create a vibrant {panelCount}-panel wordless pantomime comic strip in a clean, modern cartoon illustration style, told purely through pictures, in the tradition of silent-film slapstick.

The story (through action and expression only):
"{safeNarrative}"

Layout: {panelLayout}
- Consistent characters across panels with matching outfits and visual traits
- Clean black panel gutters/borders separating EXACTLY {panelCount} panel(s)

Draw each panel exactly as described:
{breakdown}

Visual style:
- Bold outlines, vivid colors, exaggerated facial expressions and body language
- Modern cartoon illustration (NOT manga, NOT realistic)
- Pure visual storytelling: every emotion carried by faces, gestures, and posture alone
- Every wall, sign, menu, and surface rendered as plain solid color or simple decoration
- Wordless, silent, pantomime scenes throughout
""";

        return (prompt, blunted);
    }

    private static string Blunt(string text, List<string> blunted) =>
        FlaggedTermRegex().Replace(text, match =>
        {
            var term = match.Value.ToLowerInvariant();
            if (!blunted.Contains(term, StringComparer.Ordinal))
            {
                blunted.Add(term);
            }

            return "unusual";
        });
}
