namespace PoSeeReview.Shared.Dtos;

/// <summary>
/// One comic a signed-in user has chosen to keep. Unlike the local history entry, this one
/// outlives the 24-hour comic and follows the user between devices.
/// </summary>
public class KeptComicDto
{
    public string PlaceId { get; set; } = string.Empty;

    public string RestaurantName { get; set; } = string.Empty;

    public int StrangenessScore { get; set; }

    /// <summary>
    /// Same-origin URL of the kept image. Not a blob SAS: the copy lives in a private container
    /// and is streamed by the API, so it cannot expire out from under the page the way an
    /// eight-day signature does.
    /// </summary>
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>When the user kept it.</summary>
    public DateTimeOffset KeptAt { get; set; }

    /// <summary>
    /// False when the stored copy could not be written — the row is a bookmark, and the page
    /// offers a redraw instead of a broken image.
    /// </summary>
    public bool HasImage { get; set; }
}

/// <summary>A user's collection, plus what is left of their allowance.</summary>
public class KeptComicsResponse
{
    public List<KeptComicDto> Comics { get; set; } = [];

    /// <summary>How many a user may keep in total.</summary>
    public int Limit { get; set; }

    /// <summary>How many more they can keep.</summary>
    public int Remaining { get; set; }
}
