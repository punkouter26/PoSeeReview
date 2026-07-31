using PoSeeReview.Api.Features.Restaurants;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Validation;
using Xunit;
using PoSeeReview.Shared.Contracts;
using PoSeeReview.Shared.Ids;
using PoSeeReview.Shared.Enums;

namespace PoSeeReview.Integration.Validation;

/// <summary>
/// Explicit FluentValidation rule coverage for <see cref="TakedownRequestValidator"/>.
/// Per directive #4, FluentValidation tests live in the Integration tier (not Unit).
/// </summary>
[Trait("Tier", "Integration")]
public sealed class TakedownRequestValidatorTests
{
    private readonly TakedownRequestValidator _validator = new();

    private static TakedownRequestDto Valid() => new()
    {
        PlaceId = "ChIJ-abc123",
        ContactEmail = "owner@restaurant.com",
        RequesterName = "Restaurant Owner",
        Region = "US-WA-SEATTLE",
        Reason = "We do not consent to this content appearing on your platform."
    };

    [Fact]
    public void Validate_WithValidRequest_Passes()
    {
        var result = _validator.Validate(Valid());
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_WithMissingPlaceId_Fails(string? placeId)
    {
        var dto = Valid();
        dto.PlaceId = placeId!;
        var result = _validator.Validate(dto);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(TakedownRequestDto.PlaceId));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing@")]
    [InlineData("")]
    public void Validate_WithInvalidEmail_Fails(string email)
    {
        var dto = Valid();
        dto.ContactEmail = email;
        var result = _validator.Validate(dto);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(TakedownRequestDto.ContactEmail));
    }

    [Theory]
    [InlineData("US")]            // missing state/city segments
    [InlineData("us-wa-seattle")] // lowercase not allowed
    [InlineData("USA-WA-SEATTLE")] // country must be 2 chars
    [InlineData("")]
    public void Validate_WithMalformedRegion_Fails(string region)
    {
        var dto = Valid();
        dto.Region = region;
        var result = _validator.Validate(dto);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(TakedownRequestDto.Region));
    }

    [Fact]
    public void Validate_WithMissingReason_Fails()
    {
        var dto = Valid();
        dto.Reason = string.Empty;
        var result = _validator.Validate(dto);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(TakedownRequestDto.Reason));
    }
}
