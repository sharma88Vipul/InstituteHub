using FluentValidation;
using InstituteHub.Application.Common;

namespace InstituteHub.Application.Users;

public sealed record UserListItem(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string Role,
    bool IsActive,
    bool IsCurrentUser,
    DateTimeOffset? LastLoginAt);

/// <summary>A staff member or teacher added by the owner, who shares the temporary password with them.</summary>
public sealed record NewUserRequest(string FullName, string Email, string? Phone, string Role, string Password);

public sealed class NewUserRequestValidator : AbstractValidator<NewUserRequest>
{
    public static readonly IReadOnlyList<string> AssignableRoles = [Roles.Staff, Roles.Teacher];

    public NewUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Enter the person's name.").MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter an email address; it is their login.")
            .EmailAddress().WithMessage("Enter a valid email address.").MaximumLength(150);
        RuleFor(x => x.Phone).Must(p => string.IsNullOrWhiteSpace(p) || PhoneNumber.Normalize(p) is not null)
            .WithMessage("Enter a valid mobile number, for example 98765 43210.");
        RuleFor(x => x.Role).Must(r => AssignableRoles.Contains(r))
            .WithMessage("Choose Staff (office: students, fees) or Teacher (attendance for their batches).");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Enter a temporary password.")
            .MinimumLength(8).WithMessage("The password needs at least 8 characters.");
    }
}

/// <summary>
/// Users of the institute (Settings → Users, Owner only). Implemented in Infrastructure with ASP.NET Identity.
/// There is no email service yet, so the owner sets a temporary password and shares it with the person.
/// </summary>
public interface IUserManagement
{
    Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(NewUserRequest request, CancellationToken ct = default);

    /// <summary>Deactivated users cannot sign in; open sessions end at the next check (within 30 minutes).</summary>
    Task<Result> SetActiveAsync(Guid userId, bool active, CancellationToken ct = default);

    Task<Result> ResetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default);
}
