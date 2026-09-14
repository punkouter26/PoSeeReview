using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Api.Features.Comics;

/// <summary>
/// Short links for comics. Maps <c>/api/share</c> and the public <c>/s/{code}</c> resolver
/// (NET_RULES 3.3).
/// <para>
/// A comic's canonical URL carries a Google place id — around thirty opaque characters. That is
/// fine in an href and hostile everywhere a link is actually shared: a message, a caption, a
/// screenshot someone retypes. The short form is also the only stable handle this app has for
/// counting how a comic travelled.
/// </para>
/// <para>
/// In the Comics slice rather than one of its own: a short link only ever addresses a comic, and
/// <c>/share/{placeId}/card.png</c> — the other half of sharing — was already mapped here.
/// </para>
/// </summary>
internal static class ShareLinksEndpoints
{
    /// <summary>Public prefix for a minted link. Short on purpose — it is half the point.</summary>
    public const string ResolverPrefix = "/s";

    public static IEndpointRouteBuilder MapShareLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/share").WithTags("ShareLinks");

        // Minting requires a session, like every other business slice: it writes a row.
        group.MapPost("/{placeId}", CreateAsync);

        // Resolving does not. This is the address people paste into messages, and 401-ing a
        // shared link would make the feature pointless. It sits outside /api deliberately —
        // UserAgentValidationMiddleware only lets link-preview crawlers through on non-/api
        // paths, and a short link that unfurls as a 400 is worse than no short link.
        app.MapGet($"{ResolverPrefix}/{{code}}", ResolveAsync)
            .WithTags("ShareLinks")
            .AllowAnonymous();

        return app;
    }

    private static async Task<IResult> CreateAsync(
        string placeId,
        ShareLinkRepository repository,
        ILogger<ShareLinkRepository> logger,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title: "Bad Request",
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Place ID is required",
                instance: http.Request.Path);
        }

        var origin = $"{http.Request.Scheme}://{http.Request.Host}";

        try
        {
            var code = await repository.GetOrCreateCodeAsync(PlaceId.From(placeId), http.RequestAborted);

            return Results.Ok(new ShareLinkDto
            {
                Code = code,
                ShortUrl = $"{origin}{ResolverPrefix}/{code}",
                CanonicalUrl = $"{origin}/comic/{Uri.EscapeDataString(placeId)}"
            });
        }
        catch (Exception ex)
        {
            // The caller falls back to the canonical URL, so this is a downgrade rather than a
            // failure — but it is still a storage problem worth seeing in the logs.
            logger.LogError(ex, "Failed to mint a short link for {PlaceId}", placeId);

            return Results.Problem(
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title: "Internal Server Error",
                statusCode: StatusCodes.Status500InternalServerError,
                detail: "Could not create a short link.",
                instance: http.Request.Path);
        }
    }

    /// <summary>
    /// Sends a short link on to the comic page.
    /// <para>
    /// 302, not 301. A permanent redirect is cached by the browser and by every intermediary,
    /// which would outlive a takedown: the comic would be gone and the hop would still be
    /// burned into the client. The whole point of the moderation path is that content can stop
    /// being reachable.
    /// </para>
    /// </summary>
    private static async Task<IResult> ResolveAsync(
        string code,
        ShareLinkRepository repository,
        HttpContext http)
    {
        var placeId = await repository.ResolveAsync(code, http.RequestAborted);

        if (placeId is null)
        {
            // Straight to the app rather than a 404 document. Someone following a dead short
            // link is a person holding a bad address, and the useful thing to show them is the
            // app, not an error page they cannot act on.
            return Results.Redirect("/", permanent: false);
        }

        return Results.Redirect($"/comic/{Uri.EscapeDataString(placeId.Value.Value)}", permanent: false);
    }
}
