using PoSeeReview.Api.Features.Comics;
using Xunit;

namespace PoSeeReview.Unit.Features;

/// <summary>
/// The analysis contract: the score is computed from the three ratings, and every panel reaches
/// the painter with a scene and the overlay with a caption, whatever the model returned.
/// </summary>
public sealed class ChatPromptsTests
{
    [Fact]
    public void ToAnalysis_ComputesScore_ClampsPanels_AndFillsMissingScenesFromCaptions()
    {
        var analysis = ChatPrompts.ToAnalysis(new StrangenessAnalysisResult
        {
            Absurdity = 9,
            Specificity = 9,
            StoryPotential = 14, // out of range: clamped to 10
            Narrative = "A knight plays chess with the chef.",
            Panels =
            [
                new PanelResult { Scene = "A knight in armour sits at a table.", Caption = "A knight waits." },
                new PanelResult { Scene = " ", Caption = "The chef checkmates him." },
                new PanelResult { Scene = "Dropped: only two panels are drawn.", Caption = "Dropped." }
            ]
        });

        Assert.Equal(88, analysis.StrangenessScore);
        Assert.Equal(2, analysis.PanelCount);
        Assert.Equal(["A knight waits.", "The chef checkmates him."], analysis.Captions);
        Assert.Equal(["A knight in armour sits at a table.", "The chef checkmates him."], analysis.Scenes);
    }
}
