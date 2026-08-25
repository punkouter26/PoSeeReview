using PoSeeReview.Api.Features.Comics;
using PoSeeReview.Shared.Contracts;

namespace PoSeeReview.Unit.Services;

/// <summary>
/// The receipts shown under a comic's score are quoted as the words of real reviewers about a
/// named restaurant. The model that produces them is free to paraphrase or invent, so the
/// verification gate — not the prompt — is what makes the quotes trustworthy.
/// </summary>
[Trait("Tier", "Unit")]
public class StrangenessReceiptVerificationTests
{
    private static readonly List<string> Reviews =
    [
        "The owl in the corner watched me eat my entire meal and I have never felt so judged.",
        "Waited 40 minutes for a glass of tap water that never arrived."
    ];

    [Fact]
    public void VerifyReceipts_WithVerbatimQuote_KeepsIt()
    {
        var verified = ComicGenerationService.VerifyReceipts(
            [new StrangenessReceipt("The owl in the corner watched me eat", 30)],
            Reviews);

        Assert.Single(verified);
        Assert.Equal("The owl in the corner watched me eat", verified[0].Quote);
        Assert.Equal(30, verified[0].Points);
    }

    [Fact]
    public void VerifyReceipts_WithDifferentWhitespaceAndCasing_StillKeepsIt()
    {
        // JSON round-trips and model formatting collapse or add whitespace without changing
        // what the reviewer wrote, so those differences must not read as fabrication.
        var verified = ComicGenerationService.VerifyReceipts(
            [new StrangenessReceipt("  the OWL   in the corner\n watched me eat  ", 30)],
            Reviews);

        Assert.Single(verified);
    }

    [Fact]
    public void VerifyReceipts_WithParaphrasedQuote_DropsIt()
    {
        // Nothing in the reviews says "a bird stared at me" — this is the model writing prose
        // and attributing it to a customer of a real restaurant.
        var verified = ComicGenerationService.VerifyReceipts(
            [new StrangenessReceipt("a bird stared at me the whole time", 40)],
            Reviews);

        Assert.Empty(verified);
    }

    [Fact]
    public void VerifyReceipts_WithTooShortQuote_DropsIt()
    {
        // "watched" appears verbatim but is short enough to match a paraphrase by accident,
        // which would let invented context ride along on a coincidental substring.
        var verified = ComicGenerationService.VerifyReceipts(
            [new StrangenessReceipt("watched", 10)],
            Reviews);

        Assert.Empty(verified);
    }

    [Fact]
    public void VerifyReceipts_KeepsAtMostThree_OrderedByPointsDescending()
    {
        var verified = ComicGenerationService.VerifyReceipts(
        [
            new StrangenessReceipt("Waited 40 minutes for a glass of tap water", 15),
            new StrangenessReceipt("The owl in the corner watched me eat", 45),
            new StrangenessReceipt("I have never felt so judged", 25),
            new StrangenessReceipt("that never arrived", 5)
        ], Reviews);

        Assert.Equal(3, verified.Count);
        Assert.Equal([45, 25, 15], verified.Select(r => r.Points));
    }

    [Fact]
    public void VerifyReceipts_TruncatesOverlongQuoteAfterVerifying()
    {
        var longSource = new string('a', 50) + " the owl watched " + new string('b', 200);
        var verified = ComicGenerationService.VerifyReceipts(
            [new StrangenessReceipt(longSource, 20)],
            [longSource]);

        Assert.Single(verified);
        Assert.EndsWith("…", verified[0].Quote, StringComparison.Ordinal);
        Assert.True(verified[0].Quote.Length <= 181, $"Quote was {verified[0].Quote.Length} chars");
    }

    [Fact]
    public void VerifyReceipts_WithNoReviews_ReturnsEmpty()
    {
        var verified = ComicGenerationService.VerifyReceipts(
            [new StrangenessReceipt("The owl in the corner watched me eat", 30)],
            []);

        Assert.Empty(verified);
    }
}
