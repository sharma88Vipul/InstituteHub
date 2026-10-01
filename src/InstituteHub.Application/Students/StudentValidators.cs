using FluentValidation;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;

namespace InstituteHub.Application.Students;

internal static class StudentRules
{
    public static IRuleBuilderOptions<T, string?> OptionalPhone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(p => string.IsNullOrWhiteSpace(p) || PhoneNumber.Normalize(p) is not null)
            .WithMessage("Enter a valid mobile number, for example 98765 43210.");
}

public sealed class GuardianInputValidator : AbstractValidator<GuardianInput>
{
    public GuardianInputValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Enter the guardian's name.").MaximumLength(120);
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Enter the guardian's mobile number.")
            .Must(p => PhoneNumber.Normalize(p) is not null)
            .WithMessage("Enter a valid mobile number for the guardian, for example 98765 43210.");
        RuleFor(x => x.AltPhone).OptionalPhone();
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Enter a valid email address.").MaximumLength(150);
    }
}

public sealed class AdmissionRequestValidator : AbstractValidator<AdmissionRequest>
{
    public AdmissionRequestValidator(IClock clock)
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("Enter the student's first name.").MaximumLength(60);
        RuleFor(x => x.LastName).MaximumLength(60);
        RuleFor(x => x.AdmissionNo).MaximumLength(30);
        RuleFor(x => x.Gender).MaximumLength(10);
        RuleFor(x => x.Phone).OptionalPhone();
        RuleFor(x => x.SchoolName).MaximumLength(150);
        RuleFor(x => x.ClassGrade).MaximumLength(30);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.DateOfBirth).LessThan(_ => clock.Today())
            .When(x => x.DateOfBirth is not null)
            .WithMessage("The date of birth must be in the past.");
        RuleFor(x => x.DiscountAmount).GreaterThanOrEqualTo(0).WithMessage("The discount cannot be negative.");

        RuleFor(x => x).Must(x => x.ExistingGuardianId is not null || x.NewGuardian is not null)
            .WithName("Guardian").WithMessage("Add a parent or guardian.");
        RuleFor(x => x.NewGuardian!).SetValidator(new GuardianInputValidator())
            .When(x => x.ExistingGuardianId is null && x.NewGuardian is not null);
    }
}

public sealed class UpdateStudentRequestValidator : AbstractValidator<UpdateStudentRequest>
{
    public UpdateStudentRequestValidator(IClock clock)
    {
        RuleFor(x => x.AdmissionNo).NotEmpty().WithMessage("Enter the admission number.").MaximumLength(30);
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("Enter the student's first name.").MaximumLength(60);
        RuleFor(x => x.LastName).MaximumLength(60);
        RuleFor(x => x.Gender).MaximumLength(10);
        RuleFor(x => x.Phone).OptionalPhone();
        RuleFor(x => x.SchoolName).MaximumLength(150);
        RuleFor(x => x.ClassGrade).MaximumLength(30);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.DateOfBirth).LessThan(_ => clock.Today())
            .When(x => x.DateOfBirth is not null)
            .WithMessage("The date of birth must be in the past.");
    }
}
