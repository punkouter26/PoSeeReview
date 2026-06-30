namespace Po.SeeReview.Shared.Dtos;

/// <summary>
/// Represents a restaurant owner's request to remove or update content.
/// Validation lives in <see cref="Validation.TakedownRequestValidator"/> (FluentValidation, NET_RULES 2.2).
/// </summary>
public sealed class TakedownRequestDto
{
    public string PlaceId { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string RequesterName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ProofOfAffiliationUrl { get; set; } = string.Empty;
    public string AdditionalDetails { get; set; } = string.Empty;
}
