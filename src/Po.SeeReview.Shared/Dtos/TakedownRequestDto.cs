using System.ComponentModel.DataAnnotations;

namespace Po.SeeReview.Shared.Dtos;

/// <summary>
/// Represents a restaurant owner's request to remove or update content.
/// </summary>
public sealed class TakedownRequestDto
{
    [Required]
    [MaxLength(256)]
    public string PlaceId { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string ContactEmail { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string RequesterName { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^[A-Z]{2}-[A-Z]{2}-[A-Z]+$", ErrorMessage = "Region must follow format CC-ST-CITY (e.g., US-WA-Seattle)")]
    [MaxLength(64)]
    public string Region { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(512)]
    public string ProofOfAffiliationUrl { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string AdditionalDetails { get; set; } = string.Empty;
}
