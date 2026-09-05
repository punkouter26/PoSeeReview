using FluentValidation;
using PoSeeReview.Shared.Dtos;

namespace PoSeeReview.Shared.Validation;

/// <summary>
/// FluentValidation rules for <see cref="ComicReportRequestDto"/>. DataAnnotations are avoided
/// throughout Shared so the assembly stays trimmable (NET_RULES 2.2 / 6.6).
/// <para>
/// Contact email is optional here, unlike the takedown path: requiring an address on a "this
/// comic is offensive" button suppresses exactly the reports worth having.
/// </para>
/// </summary>
public sealed class ComicReportRequestValidator : AbstractValidator<ComicReportRequestDto>
{
    /// <summary>Cap on reporter free text, matching the entity column budget.</summary>
    public const int MaxDetailsLength = 1000;

    public ComicReportRequestValidator()
    {
        RuleFor(x => x.PlaceId).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Details).MaximumLength(MaxDetailsLength);
        RuleFor(x => x.ContactEmail)
            .EmailAddress()
            .MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
    }
}
