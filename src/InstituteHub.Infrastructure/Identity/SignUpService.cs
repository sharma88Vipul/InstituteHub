using FluentValidation;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;
using InstituteHub.Application.Tenants;
using InstituteHub.Domain.Billing;
using InstituteHub.Domain.Tenants;
using InstituteHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteHub.Infrastructure.Identity;

/// <summary>
/// Design doc 6.1: in one transaction create the Tenant (Trial, 14 days), the Owner user and a
/// GROWTH trial subscription; the receipt prefix comes from the institute's initials.
/// </summary>
public sealed class SignUpService(
    AppDbContext db,
    UserManager<AppUser> users,
    IValidator<SignUpRequest> validator,
    IClock clock,
    ILogger<SignUpService> logger) : ISignUpService
{
    public const int TrialDays = 14;
    public const string TrialPlanCode = "GROWTH";

    public async Task<Result<SignUpResult>> SignUpAsync(SignUpRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Result.Invalid<SignUpResult>(validation);

        var email = request.Email.Trim();
        var instituteName = request.InstituteName.Trim();
        var ownerName = request.OwnerName.Trim();
        var phone = PhoneNumber.Normalize(request.Phone)!;

        if (await users.FindByEmailAsync(email) is not null)
        {
            return Result.Failure<SignUpResult>(Error.Conflict(
                "Email.Taken", "An account with this email already exists. Please log in instead."));
        }

        var plan = await db.SubscriptionPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == TrialPlanCode && p.IsActive, ct);
        if (plan is null)
        {
            logger.LogError("Subscription plan {Plan} is missing; run the seeder.", TrialPlanCode);
            return Result.Failure<SignUpResult>(Error.NotFound("Subscription plan"));
        }

        var now = clock.UtcNow;
        var today = clock.Today();

        var tenant = new Tenant
        {
            Name = instituteName,
            Slug = await UniqueSlugAsync(instituteName, ct),
            OwnerName = ownerName,
            Phone = phone,
            Email = email,
            ReceiptPrefix = ReceiptPrefixFor(instituteName),
            Status = TenantStatus.Trial,
            TrialEndsAt = now.AddDays(TrialDays),
        };

        var subscription = new TenantSubscription
        {
            TenantId = tenant.Id,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Trial,
            BillingCycle = BillingCycle.Monthly,
            CurrentPeriodStart = today,
            CurrentPeriodEnd = today.AddDays(TrialDays),
        };

        var owner = new AppUser
        {
            UserName = email,
            Email = email,
            // No email service yet (week 7), so the owner's address is trusted at sign-up.
            EmailConfirmed = true,
            FullName = ownerName,
            PhoneNumber = phone,
            TenantId = tenant.Id,
            LastLoginAt = now,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.Tenants.Add(tenant);
        db.TenantSubscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);

        // UserManager uses the same scoped AppDbContext, so it joins this transaction.
        var created = await users.CreateAsync(owner, request.Password);
        if (!created.Succeeded)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Result.Failure<SignUpResult>(ToErrors(created));
        }

        var roleAdded = await users.AddToRoleAsync(owner, Roles.Owner);
        if (!roleAdded.Succeeded)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Result.Failure<SignUpResult>(ToErrors(roleAdded));
        }

        await tx.CommitAsync(ct);

        logger.LogInformation("New institute {TenantId} ({Slug}) signed up", tenant.Id, tenant.Slug);
        return new SignUpResult(tenant.Id, owner.Id);
    }

    private static Error[] ToErrors(IdentityResult result) =>
        result.Errors.Select(e => Error.Validation(e.Code, e.Description)).ToArray();

    private async Task<string> UniqueSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = Slugify(name);
        var candidate = baseSlug;
        for (var i = 2; await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == candidate, ct); i++)
        {
            candidate = $"{baseSlug}-{i}";
        }
        return candidate;
    }

    /// <summary>"Sharma Classes, Ludhiana" → "sharma-classes-ludhiana".</summary>
    public static string Slugify(string name)
    {
        var chars = new List<char>(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) chars.Add(c);
            else if (chars.Count > 0 && chars[^1] != '-') chars.Add('-');
        }

        var slug = new string(chars.ToArray()).Trim('-');
        if (slug.Length > 50) slug = slug[..50].Trim('-');
        return slug.Length == 0 ? "institute" : slug;
    }

    /// <summary>"Sharma Science Classes" → "SSC"; "Excel" → "EXC". Used in receipt numbers.</summary>
    public static string ReceiptPrefixFor(string name)
    {
        var initials = name
            .Split([' ', '-', '.', ',', '&', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.FirstOrDefault(char.IsAsciiLetter))
            .Where(c => c != default)
            .Take(4)
            .ToArray();

        var prefix = initials.Length >= 2
            ? new string(initials)
            : new string(name.Where(char.IsAsciiLetter).Take(3).ToArray());

        return prefix.Length == 0 ? "INS" : prefix.ToUpperInvariant();
    }
}
