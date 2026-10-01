using InstituteHub.Application.Abstractions;
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
    int StudentCount)
{
    public int TotalSteps => 5;

    public int CompletedSteps =>
        (HasReceiptPrefix ? 1 : 0) + (HasLogo ? 1 : 0) + (HasBatch ? 1 : 0) + (HasFeePlan ? 1 : 0) + (StudentCount > 0 ? 1 : 0);

    public bool IsComplete => CompletedSteps == TotalSteps;
}

/// <summary>Read-side information about the signed-in user's institute.</summary>
public sealed class TenantService(IAppDbContext db, ITenantProvider tenant, IClock clock)
{
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

        return new OnboardingChecklist(
            HasReceiptPrefix: !string.IsNullOrWhiteSpace(t.ReceiptPrefix),
            HasLogo: !string.IsNullOrWhiteSpace(t.LogoPath),
            HasBatch: hasBatch,
            HasFeePlan: hasFeePlan,
            StudentCount: studentCount);
    }
}
