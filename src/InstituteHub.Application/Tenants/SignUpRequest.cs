using FluentValidation;
using InstituteHub.Application.Common;

namespace InstituteHub.Application.Tenants;

/// <summary>Institute sign-up (design doc 6.1).</summary>
public sealed record SignUpRequest(
    string InstituteName,
    string OwnerName,
    string Phone,
    string Email,
    string Password);

public sealed record SignUpResult(Guid TenantId, Guid UserId);

public sealed class SignUpRequestValidator : AbstractValidator<SignUpRequest>
{
    public SignUpRequestValidator()
    {
        RuleFor(x => x.InstituteName)
            .NotEmpty().WithMessage("Enter your institute's name.")
            .MaximumLength(150);

        RuleFor(x => x.OwnerName)
            .NotEmpty().WithMessage("Enter your name.")
            .MaximumLength(120);

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Enter your mobile number.")
            .Must(p => PhoneNumber.Normalize(p) is not null)
            .WithMessage("Enter a valid mobile number, for example 98765 43210.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Enter your email address.")
            .EmailAddress().WithMessage("Enter a valid email address.")
            .MaximumLength(150);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Choose a password.")
            .MinimumLength(8).WithMessage("Your password must be at least 8 characters long.");
    }
}

/// <summary>Creates the institute, its owner account and the trial subscription in one transaction.</summary>
public interface ISignUpService
{
    Task<Result<SignUpResult>> SignUpAsync(SignUpRequest request, CancellationToken ct);
}
