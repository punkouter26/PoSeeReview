using FluentValidation;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Shared.Validation;

/// <summary>
/// FluentValidation rules for <see cref="TakedownRequestDto"/> — replaces trim-unsafe
/// DataAnnotations so the Shared assembly stays trimmable (NET_RULES 2.2 / 6.6).
/// </summary>
public sealed class TakedownRequestValidator : AbstractValidator<TakedownRequestDto>
{
    public TakedownRequestValidator()
    {
        RuleFor(x => x.PlaceId).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.RequesterName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Region)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[A-Z]{2}-[A-Z]{2}-[A-Z]+$")
            .WithMessage("Region must follow format CC-ST-CITY (e.g., US-WA-Seattle)");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.ProofOfAffiliationUrl).MaximumLength(512);
        RuleFor(x => x.AdditionalDetails).MaximumLength(2000);
    }
}
