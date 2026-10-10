using System.Text.Json;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;
using InstituteHub.Domain.Students;
using InstituteHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace InstituteHub.Application.Billing;

/// <summary>Feature flag names in subscription_plans.features.</summary>
public static class PlanFeatures
{
    public const string Reminders = "reminders";
    public const string Sms = "sms";
    public const string AbsentAlerts = "absent_alerts";

    public static readonly IReadOnlyList<string> All = [Reminders, Sms, AbsentAlerts];

    /// <summary>Reads {"reminders": true, "sms": false}; only literal true switches a feature on.</summary>
    public static IReadOnlySet<string> Parse(string? json)
    {
        var on = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return on;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return on;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.True) on.Add(p.Name);
            }
        }
        catch (JsonException)
        {
            // A broken features value switches everything off rather than failing the page.
        }
        return on;
    }
}

/// <summary>The institute's plan: limits and features (design doc 5.1 Billing / 7 Feature flags).</summary>
public sealed record PlanLimits(
    string PlanCode,
    string PlanName,
    int MaxStudents,
    bool IsTrial,
    DateTimeOffset? TrialEndsAt,
    IReadOnlySet<string> Features)
{
    /// <summary>Trials try every feature; paid plans get what their plan lists.</summary>
    public bool Has(string feature) => IsTrial || Features.Contains(feature);
}

public sealed record PlanUsage(PlanLimits Limits, int ActiveStudents)
{
    public int RemainingStudents => Math.Max(0, Limits.MaxStudents - ActiveStudents);
    public double UsedPercent => Limits.MaxStudents <= 0 ? 100 : Math.Min(100, 100.0 * ActiveStudents / Limits.MaxStudents);
}

public interface IFeatureService
{
    /// <summary>The current institute's plan (cached 5 minutes per institute). Null outside an institute.</summary>
    Task<PlanLimits?> GetLimitsAsync(CancellationToken ct = default);

    Task<bool> IsEnabledAsync(string feature, CancellationToken ct = default);

    /// <summary>Plan limits plus the live count of active students (settings page, dashboard).</summary>
    Task<PlanUsage?> GetUsageAsync(CancellationToken ct = default);

    /// <summary>Fails with LimitReached when adding <paramref name="count"/> active students would exceed the plan.</summary>
    Task<Result> CanAddStudentsAsync(int count = 1, CancellationToken ct = default);

    /// <summary>Forget the cached plan, e.g. after an upgrade.</summary>
    void Invalidate();
}

/// <summary>Reads tenant_subscriptions + subscription_plans; plans rarely change, so they are cached.</summary>
public sealed class FeatureService(IAppDbContext db, ITenantProvider tenant, IMemoryCache cache) : IFeatureService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    private static string Key(Guid tenantId) => $"plan-limits:{tenantId:N}";

    public async Task<PlanLimits?> GetLimitsAsync(CancellationToken ct = default)
    {
        if (tenant.CurrentTenantId is not { } tenantId) return null;
        if (cache.TryGetValue(Key(tenantId), out PlanLimits? cached) && cached is not null) return cached;

        var institute = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Status, t.TrialEndsAt })
            .FirstOrDefaultAsync(ct);
        if (institute is null) return null;

        var plan = await db.TenantSubscriptions.AsNoTracking()
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .Select(s => new { s.Plan!.Code, s.Plan.Name, s.Plan.MaxStudents, s.Plan.Features })
            .FirstOrDefaultAsync(ct);

        var limits = plan is null
            ? new PlanLimits("NONE", "No plan", 0, institute.Status == TenantStatus.Trial, institute.TrialEndsAt, PlanFeatures.Parse(null))
            : new PlanLimits(plan.Code, plan.Name, plan.MaxStudents, institute.Status == TenantStatus.Trial,
                institute.TrialEndsAt, PlanFeatures.Parse(plan.Features));

        cache.Set(Key(tenantId), limits, CacheFor);
        return limits;
    }

    public async Task<bool> IsEnabledAsync(string feature, CancellationToken ct = default) =>
        await GetLimitsAsync(ct) is { } limits && limits.Has(feature);

    public async Task<PlanUsage?> GetUsageAsync(CancellationToken ct = default)
    {
        if (await GetLimitsAsync(ct) is not { } limits) return null;
        var active = await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct);
        return new PlanUsage(limits, active);
    }

    public async Task<Result> CanAddStudentsAsync(int count = 1, CancellationToken ct = default)
    {
        if (await GetUsageAsync(ct) is not { } usage) return Result.Failure(Error.Forbidden());
        if (usage.ActiveStudents + count <= usage.Limits.MaxStudents) return Result.Success();

        var message = count == 1 || usage.RemainingStudents == 0
            ? $"Your {usage.Limits.PlanName} plan allows up to {usage.Limits.MaxStudents} active students and you have {usage.ActiveStudents}. " +
              "Upgrade your plan or mark students who left as 'Left'."
            : $"Your {usage.Limits.PlanName} plan has room for {usage.RemainingStudents} more active student(s), but {count} would be added. " +
              "Import fewer students, upgrade your plan or mark students who left as 'Left'.";
        return Result.Failure(Error.LimitReached(message));
    }

    public void Invalidate()
    {
        if (tenant.CurrentTenantId is { } tenantId) cache.Remove(Key(tenantId));
    }
}
