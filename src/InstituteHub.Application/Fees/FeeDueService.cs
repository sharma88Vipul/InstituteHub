using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Fees;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Application.Fees;

/// <summary>Reading and maintaining fee dues. Payments against dues arrive in week 6.</summary>
public sealed class FeeDueService(IAppDbContext db, ICurrentUser user, IClock clock)
{
    /// <summary>All dues of a student, oldest first. Null for users who may not see fees (teachers).</summary>
    public async Task<StudentFees?> GetStudentFeesAsync(Guid studentId, CancellationToken ct = default)
    {
        if (!user.CanManage()) return null;

        var today = clock.Today();
        var rows = await db.FeeDues.AsNoTracking()
            .Where(d => d.StudentId == studentId)
            .OrderBy(d => d.DueDate).ThenBy(d => d.Title)
            .Select(d => new
            {
                d.Id, d.Title, d.DueDate, d.Amount, d.DiscountAmount, d.PaidAmount, d.Status,
                BatchName = db.Enrollments.Where(e => e.Id == d.EnrollmentId).Select(e => e.Batch!.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var dues = rows
            .Select(r => new FeeDueItem(r.Id, r.Title, r.BatchName ?? "-", r.DueDate, r.Amount, r.DiscountAmount, r.PaidAmount, r.Status,
                IsOverdue: r.DueDate < today && r.Status is FeeDueStatus.Pending or FeeDueStatus.PartiallyPaid))
            .ToList();

        var open = dues.Where(d => d.Status != FeeDueStatus.Waived).ToList();
        var totals = new FeeTotals(
            Gross: open.Sum(d => d.Amount),
            Discount: open.Sum(d => d.DiscountAmount),
            Paid: dues.Sum(d => d.PaidAmount),
            Balance: open.Sum(d => d.Balance),
            Overdue: open.Where(d => d.IsOverdue).Sum(d => d.Balance));

        return new StudentFees(totals, dues);
    }

    /// <summary>
    /// Creates dues that are missing: enrollments that have none yet (e.g. created before week 5), and monthly
    /// dues up to <paramref name="asOf"/>'s month. Safe to run any number of times. The daily MonthlyDueJob
    /// (week 7) calls this per institute.
    /// </summary>
    public async Task<Result<DueGenerationResult>> GenerateMissingDuesAsync(DateOnly? asOf = null, CancellationToken ct = default)
    {
        if (!user.CanManage() && user.IsAuthenticated) return Result.Failure<DueGenerationResult>(Error.Forbidden());

        var today = asOf ?? clock.Today();
        var enrollments = await db.Enrollments.AsNoTracking()
            .Where(e => e.Status == EnrollmentStatus.Active || e.EndedOn != null)
            .Include(e => e.FeePlan)
            .ToListAsync(ct);

        var existing = await db.FeeDues.AsNoTracking()
            .Select(d => new { d.EnrollmentId, d.PeriodKey })
            .ToListAsync(ct);
        var byEnrollment = existing
            .GroupBy(d => d.EnrollmentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PeriodKey).OfType<string>().ToHashSet());

        var created = 0;
        foreach (var enrollment in enrollments)
        {
            if (enrollment.FeePlan is not { } plan) continue;
            var hasDues = byEnrollment.TryGetValue(enrollment.Id, out var keys);

            IReadOnlyList<FeeDue> dues = plan.BillingType switch
            {
                BillingType.Monthly => FeeDueGenerator.Monthly(enrollment, plan, today, keys ?? new HashSet<string>()),
                _ when !hasDues && enrollment.Status == EnrollmentStatus.Active => FeeDueGenerator.ForNewEnrollment(enrollment, plan, today),
                _ => [],
            };

            foreach (var due in dues)
            {
                db.FeeDues.Add(due);   // explicit Add: keys are set in C#, see EnrollmentService
                created++;
            }
        }

        if (created > 0) await db.SaveChangesAsync(ct);
        return new DueGenerationResult(enrollments.Count, created);
    }

    /// <summary>Waives an unpaid due (Owner only, design doc 5.2).</summary>
    public async Task<Result> WaiveAsync(Guid dueId, CancellationToken ct = default)
    {
        if (!user.IsInRole(Roles.Owner)) return Result.Failure(Error.Forbidden("Only the institute owner can waive fees."));

        var due = await db.FeeDues.FirstOrDefaultAsync(d => d.Id == dueId, ct);
        if (due is null) return Result.Failure(Error.NotFound("Fee due"));
        if (due.Status == FeeDueStatus.Waived) return Result.Success();
        if (due.PaidAmount > 0)
            return Result.Failure(Error.Conflict("FeeDue.HasPayments", "This due already has payments, so it cannot be waived."));

        due.Waive();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (db is DbContext context) context.ChangeTracker.Clear();
            return Result.Failure(Error.ConcurrencyConflict);
        }
        return Result.Success();
    }
}
