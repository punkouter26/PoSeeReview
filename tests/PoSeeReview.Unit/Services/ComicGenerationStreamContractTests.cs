using System.Text.Json;
using PoSeeReview.Client.Services;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Enums;
using Xunit;

namespace PoSeeReview.Unit.Services;

/// <summary>
/// The streaming endpoint writes each event with <c>JsonSerializerDefaults.Web</c>; the trimmed
/// WASM client reads it back through the source-generated <c>AppJsonContext</c>. A naming-policy
/// or enum-representation mismatch between those two does not throw — it produces an envelope of
/// default values, so the stepper would silently sit on phase zero and the comic would arrive as
/// null. That failure is invisible without this test.
/// </summary>
[Trait("Tier", "Unit")]
[Trait("Suite", "CriticalPath")]
public class ComicGenerationStreamContractTests
{
    /// <summary>Mirrors <c>ComicsEndpoints.StreamJsonOptions</c>.</summary>
    private static readonly JsonSerializerOptions ServerOptions = new(JsonSerializerDefaults.Web);

    private static ComicGenerationEventDto RoundTrip(ComicGenerationEventDto evt)
    {
        var json = JsonSerializer.Serialize(evt, ServerOptions);
        var parsed = JsonSerializer.Deserialize(json, AppJsonContext.Default.ComicGenerationEventDto);
        Assert.NotNull(parsed);
        return parsed;
    }

    [Theory]
    [InlineData(ComicGenerationPhase.CacheHit)]
    [InlineData(ComicGenerationPhase.FetchingReviews)]
    [InlineData(ComicGenerationPhase.AnalyzingStrangeness)]
    [InlineData(ComicGenerationPhase.GeneratingArtwork)]
    [InlineData(ComicGenerationPhase.ComposingStrip)]
    [InlineData(ComicGenerationPhase.Publishing)]
    public void PhaseEvent_SurvivesTheRoundTrip(ComicGenerationPhase phase)
    {
        var parsed = RoundTrip(new ComicGenerationEventDto
        {
            Kind = ComicGenerationEventDto.PhaseKind,
            Phase = phase
        });

        Assert.Equal(ComicGenerationEventDto.PhaseKind, parsed.Kind);
        Assert.Equal(phase, parsed.Phase);
    }

    [Fact]
    public void CompleteEvent_CarriesTheComic()
    {
        var parsed = RoundTrip(new ComicGenerationEventDto
        {
            Kind = ComicGenerationEventDto.CompleteKind,
            Comic = new ComicDto
            {
                ComicId = "comic-1",
                PlaceId = "place-1",
                RestaurantName = "The Owl Cafe",
                Narrative = "An owl judged the soup.",
                StrangenessScore = 87,
                BlobUrl = "https://example.invalid/comic.png"
            }
        });

        Assert.Equal(ComicGenerationEventDto.CompleteKind, parsed.Kind);
        Assert.NotNull(parsed.Comic);
        Assert.Equal("The Owl Cafe", parsed.Comic.RestaurantName);
        Assert.Equal(87, parsed.Comic.StrangenessScore);
        Assert.Equal("An owl judged the soup.", parsed.Comic.Narrative);
        Assert.Equal("https://example.invalid/comic.png", parsed.Comic.BlobUrl);
    }

    [Fact]
    public void ErrorEvent_KeepsTheStatusTheClientBranchesOn()
    {
        // 422 is what drives the "Too Normal for a Comic!" copy. Losing it downgrades a tailored
        // message to the generic failure text.
        var parsed = RoundTrip(new ComicGenerationEventDto
        {
            Kind = ComicGenerationEventDto.ErrorKind,
            ErrorStatus = 422,
            ErrorTitle = "Unprocessable Entity",
            ErrorDetail = "Too ordinary."
        });

        Assert.Equal(ComicGenerationEventDto.ErrorKind, parsed.Kind);
        Assert.Equal(422, parsed.ErrorStatus);
        Assert.Equal("Too ordinary.", parsed.ErrorDetail);
    }

    [Fact]
    public void SerializedEvent_FitsOnASingleSseDataLine()
    {
        // The client parses one data: line as one JSON object. An embedded newline would split
        // the payload across frames and every event would fail to parse.
        var json = JsonSerializer.Serialize(new ComicGenerationEventDto
        {
            Kind = ComicGenerationEventDto.CompleteKind,
            Comic = new ComicDto
            {
                RestaurantName = "Line\nBreak Cafe",
                Narrative = "First line.\nSecond line."
            }
        }, ServerOptions);

        Assert.DoesNotContain('\n', json);
        Assert.DoesNotContain('\r', json);
    }
}
