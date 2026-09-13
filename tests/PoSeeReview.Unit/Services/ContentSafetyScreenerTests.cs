using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PoSeeReview.Api.Features.Moderation;
using PoSeeReview.Shared.Contracts;
using Xunit;

namespace PoSeeReview.Unit.Services;

/// <summary>
/// The pre-publish content screen.
/// <para>
/// The tests that matter most here are the ones asserting what it does <em>not</em> block. This
/// app exists to mine one-star restaurant reviews; a screen that refused every unpleasant
/// narrative would refuse the product. The design is that allegation-shaped language publishes
/// and lands in the moderation queue, and only categories with no legitimate reading are
/// refused outright.
/// </para>
/// </summary>
[Trait("Tier", "Unit")]
[Trait("Suite", "CriticalPath")]
public class ContentSafetyScreenerTests
{
    private static LexicalContentSafetyScreener CreateScreener(bool enabled = true) =>
        new(
            Options.Create(new ModerationOptions { ScreenGeneratedContent = enabled }),
            NullLogger<LexicalContentSafetyScreener>.Instance);

    [Theory]
    [InlineData("The soup arrived cold, twice, and the waiter apologised in verse.")]
    [InlineData("Every table was occupied by a man in an identical grey hat. Nobody explained.")]
    [InlineData("Ratatouille was the only thing on the menu and it was served in a shoe.")]
    [InlineData("The chef assaulted my expectations of what a dumpling could be.")]
    [InlineData("The garlic bread stole the show and the tiramisu never recovered.")]
    public async Task ScreenAsync_OrdinaryStrangeness_IsAllowed(string narrative)
    {
        var result = await CreateScreener().ScreenAsync(narrative);

        // Two different traps. "Ratatouille" is why the patterns are word-boundary matched.
        // "assaulted my expectations" and "stole the show" are why those words are not in the
        // list at all - a boundary does not help when the whole word is the figurative one, and
        // a queue full of metaphors is a queue nobody reads.
        Assert.Equal(ContentScreenOutcome.Allow, result.Outcome);
    }

    [Theory]
    [InlineData("Three reviewers reported food poisoning after the buffet.")]
    [InlineData("A rat walked across the counter while we waited.")]
    [InlineData("The health department shut them down last spring, apparently.")]
    [InlineData("There is apparently a lawsuit pending against the owner.")]
    public async Task ScreenAsync_ClaimShapedLanguage_IsFlaggedButStillPublishes(string narrative)
    {
        var result = await CreateScreener().ScreenAsync(narrative);

        // Flagged, not blocked. This is ordinary content in the reviews the app mines, and it is
        // also the shape of a defamation problem once a model restates it about a named
        // business — so it publishes and a human sees it.
        Assert.Equal(ContentScreenOutcome.Flag, result.Outcome);
        Assert.False(result.IsBlocked);
        Assert.Equal("allegation", result.Category);
    }

    [Fact]
    public async Task ScreenAsync_ProhibitedCategory_IsBlocked()
    {
        var result = await CreateScreener().ScreenAsync("The mural depicted child abuse in four panels.");

        Assert.Equal(ContentScreenOutcome.Block, result.Outcome);
        Assert.True(result.IsBlocked);
    }

    [Fact]
    public async Task ScreenAsync_ProhibitedBeatsAllegation()
    {
        // Both patterns match. The refusal has to win, or a blocked narrative would publish with
        // a flag on it.
        var result = await CreateScreener().ScreenAsync("A rat, and a mural depicting child abuse.");

        Assert.Equal(ContentScreenOutcome.Block, result.Outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ScreenAsync_EmptyText_IsAllowed(string narrative) =>
        Assert.Equal(ContentScreenOutcome.Allow, (await CreateScreener().ScreenAsync(narrative)).Outcome);

    [Fact]
    public async Task ScreenAsync_WhenDisabled_AllowsEvenProhibitedContent()
    {
        var result = await CreateScreener(enabled: false).ScreenAsync("child abuse");

        // The switch genuinely switches it off. Worth asserting so nobody assumes the screen is
        // running when the option says it is not.
        Assert.Equal(ContentScreenOutcome.Allow, result.Outcome);
    }
}
