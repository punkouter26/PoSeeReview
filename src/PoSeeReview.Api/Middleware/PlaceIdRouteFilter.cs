using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Middleware;

/// <summary>
/// Rejects a malformed place id in the route with a 400 before any handler sees it. The raw
/// value otherwise reaches a table RowKey, a metric dimension and the keyed Google Places URL.
/// Routes without the named values pass straight through, so it can sit on a whole group.
/// </summary>
internal static class PlaceIdRouteFilter
{
    public static TBuilder RequireValidPlaceId<TBuilder>(this TBuilder builder, params string[] routeKeys)
        where TBuilder : IEndpointConventionBuilder
    {
        string[] keys = routeKeys is [] ? ["placeId"] : routeKeys;

        return builder.AddEndpointFilter(async (context, next) =>
            keys.All(key => !context.HttpContext.Request.RouteValues.TryGetValue(key, out var value)
                            || PlaceId.IsValid(value as string))
                ? await next(context)
                : Results.Problem(
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    title: "Bad Request",
                    statusCode: StatusCodes.Status400BadRequest,
                    detail: "Place ID is not valid",
                    instance: context.HttpContext.Request.Path));
    }
}
