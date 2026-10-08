using FluentValidation;
using PoSeeReview.Shared.Dtos;
using PoSeeReview.Shared.Ids;

namespace PoSeeReview.Shared.Validation;

/// <summary>
/// FluentValidation rules for <see cref="TakedownRequestDto"/> — replaces trim-unsafe
/// DataAnnotations so the Shared assembly stays trimmable (NET_RULES 2.2 / 6.6).
/// </summary>
public sealed class TakedownRequestValidator : AbstractValidator<TakedownRequestDto>
{
    public TakedownRequestValidator()
    {
        RuleFor(x => x.PlaceId).NotEmpty().Matches(PlaceId.Pattern);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.RequesterName).NotEmpty().MaximumLength(256);
        // Informational only: the erase finds the board row by place id, whatever its region.
        RuleFor(x => x.Region).MaximumLength(64);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.ProofOfAffiliationUrl).MaximumLength(512);
        RuleFor(x => x.AdditionalDetails).MaximumLength(2000);
    }
}
