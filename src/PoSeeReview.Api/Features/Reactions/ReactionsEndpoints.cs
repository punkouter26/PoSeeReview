using PoSeeReview.Api.Identity;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Reactions;

/// <summary>
/// Comic reactions slice. Maps <c>/api/reactions</c> (NET_RULES 3.3).
/// <para>
/// A closed set of four reactions and no free text: a comment box on AI-generated content about
/// a named business is a moderation surface this app has no queue for. Reacting is the cheapest
/// thing a viewer can do that gives the comic an audience.
/// </para>
/// </summary>
internal static class ReactionsEndpoints
{
    public static IEndpointRouteBuilder MapReactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reactions").WithTags("Reactions");

        group.MapGet("/{placeId}", GetReactions);
        group.MapPost("/{placeId}", SetReaction);

        return app;
    }

    private static async Task<IResult> GetReactions(
        string placeId,
        ReactionRepository repository,
        ICurrentRequestIdentityAccessor identityAccessor,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return BadPlaceId(http);
        }

        var counts = await repository.GetAsync(
            PlaceId.From(placeId), identityAccessor.GetCurrentUserId(), http.RequestAborted);

        return Results.Ok(counts);
    }

    private static async Task<IResult> SetReaction(
        string placeId,
        ReactionRequestDto request,
        ReactionRepository repository,
        ICurrentRequestIdentityAccessor identityAccessor,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return BadPlaceId(http);
        }

        // A null reaction is the documented "withdraw mine" case, so only a value outside the
        // enum is a bad request.
        if (request.Reaction is { } kind && !Enum.IsDefined(kind))
        {
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title: "Bad Request",
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Unknown reaction.",
                instance: http.Request.Path);
        }

        var counts = await repository.SetAsync(
            PlaceId.From(placeId), identityAccessor.GetCurrentUserId(), request.Reaction, http.RequestAborted);

        return Results.Ok(counts);
    }

    private static IResult BadPlaceId(HttpContext http) => Results.Problem(
        type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        title: "Bad Request",
        statusCode: StatusCodes.Status400BadRequest,
        detail: "Place ID is required",
        instance: http.Request.Path);
}
