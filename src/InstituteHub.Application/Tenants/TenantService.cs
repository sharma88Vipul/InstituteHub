using FluentValidation;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;
using InstituteHub.Application.Users;
using InstituteHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Application.Tenants;

public sealed record TenantSummary(
    Guid Id,
    string Name,
    TenantStatus Status,
    DateTimeOffset? TrialEndsAt,
    int? TrialDaysLeft,
    string ReceiptPrefix,
    string? PlanName);

public sealed record OnboardingChecklist(
    bool HasReceiptPrefix,
    bool HasLogo,
    bool HasBatch,
    bool HasFeePlan,
    int StudentCount,
    bool HasTeam)
{
    public int TotalSteps => 6;

    public int CompletedSteps =>
        (HasReceiptPrefix ? 1 : 0) + (HasLogo ? 1 : 0) + (HasBatch ? 1 : 0) + (HasFeePlan ? 1 : 0)
        + (StudentCount > 0 ? 1 : 0) + (HasTeam ? 1 : 0);

    public bool IsComplete => CompletedSteps == TotalSteps;
}

/// <summary>Institute profile shown on receipts (Settings → Institute).</summary>
public sealed record InstituteProfile(
    string Name,
    string OwnerName,
    string Phone,
    string? Email,
    string? AddressLine,
    string? City,
    string? State,
    string? Pincode,
    string? Gstin,
    string ReceiptPrefix,
    bool HasLogo);

public sealed record InstituteProfileRequest(
    string Name,
    string OwnerName,
    string Phone,
    string? Email,
    string? AddressLine,
    string? City,
    string? State,
    string? Pincode,
    string? Gstin,
    string ReceiptPrefix);

public sealed class InstituteProfileRequestValidator : AbstractValidator<InstituteProfileRequest>
{
    public InstituteProfileRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter the institute name.").MaximumLength(150);
        RuleFor(x => x.OwnerName).NotEmpty().WithMessage("Enter the owner's name.").MaximumLength(120);
        RuleFor(x => x.Phone).Must(p => PhoneNumber.Normalize(p) is not null)
            .WithMessage("Enter a valid mobile number, for example 98765 43210.");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Enter a valid email address.").MaximumLength(150);
        RuleFor(x => x.AddressLine).MaximumLength(200);
        RuleFor(x => x.City).MaximumLength(80);
        RuleFor(x => x.State).MaximumLength(80);
        RuleFor(x => x.Pincode).Matches("^[1-9][0-9]{5}$").When(x => !string.IsNullOrWhiteSpace(x.Pincode))
            .WithMessage("A PIN code has 6 digits.");
        RuleFor(x => x.Gstin).Matches("^[0-9]{2}[A-Za-z0-9]{13}$").When(x => !string.IsNullOrWhiteSpace(x.Gstin))
            .WithMessage("A GSTIN has 15 characters, e.g. 03ABCDE1234F1Z5.");
        RuleFor(x => x.ReceiptPrefix).NotEmpty().WithMessage("Enter a receipt prefix.")
            .Matches("^[A-Za-z0-9]{2,10}$").WithMessage("The receipt prefix must be 2 to 10 letters or digits, e.g. SCL.");
    }
}

/// <summary>The signed-in user's institute: summary, onboarding checklist, profile and logo.</summary>
public sealed class TenantService(
    IAppDbContext db,
    ITenantProvider tenant,
    ICurrentUser user,
    IClock clock,
    IFileStorage files,
    IUserDirectory users,
    IValidator<InstituteProfileRequest> profileValidator)
{
    public const long MaxLogoBytes = 512 * 1024;

    private static readonly Dictionary<string, string> LogoTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
    };

    /// <summary>The current institute, or null when the user is not linked to one.</summary>
    public async Task<TenantSummary?> GetCurrentAsync(CancellationToken ct = default)
    {
        if (tenant.CurrentTenantId is not { } tenantId) return null;

        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        if (t is null) return null;

        var planName = await db.TenantSubscriptions.AsNoTracking()
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .Select(s => s.Plan!.Name)
            .FirstOrDefaultAsync(ct);

        int? trialDaysLeft = t.Status == TenantStatus.Trial && t.TrialEndsAt is { } end
            ? Math.Max(0, (int)Math.Ceiling((end - clock.UtcNow).TotalDays))
            : null;

        return new TenantSummary(t.Id, t.Name, t.Status, t.TrialEndsAt, trialDaysLeft, t.ReceiptPrefix, planName);
    }

    /// <summary>Setup steps shown on /onboarding after sign-up.</summary>
    public async Task<OnboardingChecklist?> GetOnboardingChecklistAsync(CancellationToken ct = default)
    {
        if (tenant.CurrentTenantId is not { } tenantId) return null;

        var t = await db.Tenants.AsNoTracking()
            .Where(x => x.Id == tenantId)
            .Select(x => new { x.ReceiptPrefix, x.LogoPath })
            .FirstOrDefaultAsync(ct);
        if (t is null) return null;

        // Sequential awaits on purpose: one DbContext must not run queries in parallel.
        var hasBatch = await db.Batches.AnyAsync(ct);
        var hasFeePlan = await db.FeePlans.AnyAsync(ct);
        var studentCount = await db.Students.CountAsync(ct);
        var userCount = await users.CountActiveUsersAsync(ct);

        return new OnboardingChecklist(
            HasReceiptPrefix: !string.IsNullOrWhiteSpace(t.ReceiptPrefix),
            HasLogo: !string.IsNullOrWhiteSpace(t.LogoPath),
            HasBatch: hasBatch,
            HasFeePlan: hasFeePlan,
            StudentCount: studentCount,
            HasTeam: userCount > 1);
    }

    // ------------------------------------------------------------------ profile (Owner)

    public async Task<InstituteProfile?> GetProfileAsync(CancellationToken ct = default)
    {
        if (tenant.CurrentTenantId is not { } tenantId) return null;
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new InstituteProfile(t.Name, t.OwnerName, t.Phone, t.Email, t.AddressLine, t.City, t.State,
                t.Pincode, t.Gstin, t.ReceiptPrefix, t.LogoPath != null))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Updates the profile (Owner only). A new receipt prefix applies to receipts issued from now on.</summary>
    public async Task<Result> UpdateProfileAsync(InstituteProfileRequest request, CancellationToken ct = default)
    {
        if (!user.IsInRole(Roles.Owner)) return Result.Failure(Error.Forbidden("Only the institute owner can change these settings."));
        var validation = await profileValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Result.Invalid(validation);

        if (await CurrentTenantAsync(ct) is not { } t) return Result.Failure(Error.NotFound("Institute"));
        t.Name = request.Name.Trim();
        t.OwnerName = request.OwnerName.Trim();
        t.Phone = PhoneNumber.Normalize(request.Phone)!;
        t.Email = Clean(request.Email);
        t.AddressLine = Clean(request.AddressLine);
        t.City = Clean(request.City);
        t.State = Clean(request.State);
        t.Pincode = Clean(request.Pincode);
        t.Gstin = Clean(request.Gstin)?.ToUpperInvariant();
        t.ReceiptPrefix = request.ReceiptPrefix.Trim().ToUpperInvariant();

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Saves a PNG or JPEG logo (max 512 KB) printed on receipts (Owner only).</summary>
    public async Task<Result> SetLogoAsync(Stream content, string contentType, long length, CancellationToken ct = default)
    {
        if (!user.IsInRole(Roles.Owner)) return Result.Failure(Error.Forbidden("Only the institute owner can change the logo."));
        if (!LogoTypes.TryGetValue(contentType, out var extension))
            return Result.Failure(Error.Validation("Logo", "Upload the logo as a PNG or JPG image."));
        if (length <= 0 || length > MaxLogoBytes)
            return Result.Failure(Error.Validation("Logo", "The logo must be smaller than 512 KB."));

        if (await CurrentTenantAsync(ct) is not { } t) return Result.Failure(Error.NotFound("Institute"));

        var oldKey = t.LogoPath;
        t.LogoPath = await files.SaveAsync($"logos/{t.Id:N}/logo-{Guid.CreateVersion7():N}{extension}", content, ct);
        await db.SaveChangesAsync(ct);

        if (oldKey is not null) await files.DeleteAsync(oldKey, ct);
        return Result.Success();
    }

    public async Task<Result> RemoveLogoAsync(CancellationToken ct = default)
    {
        if (!user.IsInRole(Roles.Owner)) return Result.Failure(Error.Forbidden("Only the institute owner can change the logo."));
        if (await CurrentTenantAsync(ct) is not { } t) return Result.Failure(Error.NotFound("Institute"));
        if (t.LogoPath is not { } key) return Result.Success();

        t.LogoPath = null;
        await db.SaveChangesAsync(ct);
        await files.DeleteAsync(key, ct);
        return Result.Success();
    }

    /// <summary>The current institute's logo bytes and content type, or null.</summary>
    public async Task<(byte[] Bytes, string ContentType)?> GetLogoAsync(CancellationToken ct = default)
    {
        if (tenant.CurrentTenantId is not { } tenantId) return null;
        var key = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.LogoPath).FirstOrDefaultAsync(ct);
        if (key is null || await files.ReadAsync(key, ct) is not { } bytes) return null;
        return (bytes, key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg");
    }

    private async Task<Tenant?> CurrentTenantAsync(CancellationToken ct)
    {
        if (tenant.CurrentTenantId is not { } tenantId) return null;
        // Blazor keeps one DbContext per circuit: start from the saved row, not an older tracked copy.
        if (db is DbContext context) context.ChangeTracker.Clear();
        return await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
