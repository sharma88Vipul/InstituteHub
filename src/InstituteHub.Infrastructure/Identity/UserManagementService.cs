using FluentValidation;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;
using InstituteHub.Application.Users;
using InstituteHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteHub.Infrastructure.Identity;

/// <summary>
/// Settings → Users (Owner only). Identity users are not covered by the tenant query filters, so every query here
/// filters on the current institute explicitly, and a user of another institute is reported as not found.
/// </summary>
public sealed class UserManagementService(
    AppDbContext db,
    UserManager<AppUser> users,
    ITenantProvider tenant,
    ICurrentUser currentUser,
    IClock clock,
    IValidator<NewUserRequest> validator,
    ILogger<UserManagementService> logger) : IUserManagement
{
    private bool IsOwner => currentUser.IsInRole(Roles.Owner);

    public async Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken ct = default)
    {
        if (!IsOwner || tenant.CurrentTenantId is not { } tenantId) return [];

        var rows = await (
                from u in db.Users.AsNoTracking()
                where u.TenantId == tenantId
                select new
                {
                    u.Id, u.FullName, u.Email, u.PhoneNumber, u.IsActive, u.LastLoginAt,
                    Roles = (from ur in db.UserRoles
                             join r in db.Roles on ur.RoleId equals r.Id
                             where ur.UserId == u.Id
                             select r.Name).ToList(),
                })
            .ToListAsync(ct);

        return rows
            .Select(u => new UserListItem(
                u.Id, u.FullName, u.Email ?? "", u.PhoneNumber, MainRole(u.Roles), u.IsActive,
                u.Id == currentUser.UserId, u.LastLoginAt))
            .OrderBy(u => RoleOrder(u.Role)).ThenBy(u => u.FullName)
            .ToList();
    }

    public async Task<Result<Guid>> CreateAsync(NewUserRequest request, CancellationToken ct = default)
    {
        if (!IsOwner || tenant.CurrentTenantId is not { } tenantId)
            return Result.Failure<Guid>(Error.Forbidden("Only the institute owner can add users."));

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Result.Invalid<Guid>(validation);

        var email = request.Email.Trim();
        if (await users.FindByEmailAsync(email) is not null)
            return Result.Failure<Guid>(Error.Conflict("Email.Taken", "Someone already has an account with this email."));

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            // No email service yet: the owner vouches for the address and shares the temporary password.
            EmailConfirmed = true,
            FullName = request.FullName.Trim(),
            PhoneNumber = PhoneNumber.Normalize(request.Phone),
            TenantId = tenantId,
            LockoutEnabled = true,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded) return Fail<Guid>(created);

        var role = await users.AddToRoleAsync(user, request.Role);
        if (!role.Succeeded)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Fail<Guid>(role);
        }

        await tx.CommitAsync(ct);
        logger.LogInformation("User {UserId} added as {Role}", user.Id, request.Role);
        return user.Id;
    }

    public async Task<Result> SetActiveAsync(Guid userId, bool active, CancellationToken ct = default)
    {
        if (!IsOwner) return Result.Failure(Error.Forbidden("Only the institute owner can change users."));
        if (userId == currentUser.UserId) return Result.Failure(Error.Validation("User", "You cannot deactivate your own account."));
        if (await FindAsync(userId) is not { } user) return Result.Failure(Error.NotFound("User"));
        if (await users.IsInRoleAsync(user, Roles.Owner))
            return Result.Failure(Error.Validation("User", "The institute owner cannot be deactivated."));

        user.IsActive = active;
        // Lockout blocks sign-in; a new security stamp ends sessions that are already open.
        user.LockoutEnabled = true;
        user.LockoutEnd = active ? null : clock.UtcNow.AddYears(100);
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded) return Fail(updated);
        await users.UpdateSecurityStampAsync(user);

        logger.LogInformation("User {UserId} {Action} at {At}", user.Id, active ? "activated" : "deactivated", clock.UtcNow);
        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default)
    {
        if (!IsOwner) return Result.Failure(Error.Forbidden("Only the institute owner can reset passwords."));
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return Result.Failure(Error.Validation("Password", "The password needs at least 8 characters."));
        if (await FindAsync(userId) is not { } user) return Result.Failure(Error.NotFound("User"));

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, newPassword);
        if (!reset.Succeeded) return Fail(reset);

        await users.ResetAccessFailedCountAsync(user);
        logger.LogInformation("Password reset for user {UserId} by the owner", user.Id);
        return Result.Success();
    }

    private async Task<AppUser?> FindAsync(Guid userId)
    {
        if (tenant.CurrentTenantId is not { } tenantId) return null;
        var user = await users.FindByIdAsync(userId.ToString());
        return user?.TenantId == tenantId ? user : null;
    }

    private static string MainRole(IEnumerable<string?> roles)
    {
        var set = roles.OfType<string>().ToHashSet();
        return set.Contains(Roles.Owner) ? Roles.Owner
            : set.Contains(Roles.Staff) ? Roles.Staff
            : set.Contains(Roles.Teacher) ? Roles.Teacher
            : "-";
    }

    private static int RoleOrder(string role) => role switch
    {
        Roles.Owner => 0,
        Roles.Staff => 1,
        Roles.Teacher => 2,
        _ => 3,
    };

    private static Result<T> Fail<T>(IdentityResult result) =>
        Result.Failure<T>(result.Errors.Select(e => Error.Validation(e.Code, e.Description)).ToArray());

    private static Result Fail(IdentityResult result) =>
        Result.Failure(result.Errors.Select(e => Error.Validation(e.Code, e.Description)).ToArray());
}
