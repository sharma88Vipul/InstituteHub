using FluentValidation;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;
using InstituteHub.Domain.Fees;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Application.Fees;

/// <summary>
/// Fee plan maintenance. Generating dues from a plan (FeeDueGenerator) is added in week 5;
/// editing a plan never changes dues that already exist.
/// </summary>
public sealed class FeePlanService(IAppDbContext db, ICurrentUser user, IValidator<FeePlanRequest> validator)
{
    public async Task<IReadOnlyList<FeePlanItem>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var plans = db.FeePlans.AsNoTracking();
        if (!includeInactive) plans = plans.Where(p => p.IsActive);

        return await plans
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.Name)
            .Select(p => new FeePlanItem(p.Id, p.Name, p.BillingType, p.TotalAmount, p.MonthlyAmount,
                p.InstalmentCount, p.InstalmentIntervalMonths, p.DueDayOfMonth, p.IsActive))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FeePlanOption>> GetOptionsAsync(CancellationToken ct = default) =>
        (await ListAsync(includeInactive: false, ct)).Select(p => new FeePlanOption(p.Id, p.Name, p.Summary)).ToList();

    public async Task<Result<Guid>> CreateAsync(FeePlanRequest request, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure<Guid>(Error.Forbidden());
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Result.Invalid<Guid>(validation);

        var plan = new FeePlan { IsActive = true };
        Apply(plan, request);
        db.FeePlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return plan.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, FeePlanRequest request, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure(Error.Forbidden());
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Result.Invalid(validation);

        var plan = await db.FeePlans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return Result.Failure(Error.NotFound("Fee plan"));

        Apply(plan, request);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure(Error.Forbidden());
        var plan = await db.FeePlans.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null) return Result.Failure(Error.NotFound("Fee plan"));

        plan.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static void Apply(FeePlan plan, FeePlanRequest r)
    {
        plan.Name = r.Name.Trim();
        plan.BillingType = r.BillingType;
        plan.DueDayOfMonth = r.DueDayOfMonth;
        plan.TotalAmount = r.BillingType == BillingType.Monthly ? 0 : r.TotalAmount;
        plan.MonthlyAmount = r.BillingType == BillingType.Monthly ? r.MonthlyAmount : null;
        plan.InstalmentCount = r.BillingType == BillingType.Instalments ? r.InstalmentCount : null;
        plan.InstalmentIntervalMonths = r.BillingType == BillingType.Instalments ? (r.InstalmentIntervalMonths ?? 1) : null;
    }
}
