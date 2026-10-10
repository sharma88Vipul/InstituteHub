using FluentValidation;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;
using InstituteHub.Application.Users;
using InstituteHub.Domain.Fees;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Application.Payments;

/// <summary>
/// Fee collection (design doc 6.4 / 11.6). A saved payment is never edited: to correct a mistake it is
/// cancelled (allocations reversed, receipt number kept with a CANCELLED mark) and a new one is recorded.
/// </summary>
public sealed class PaymentService(
    IAppDbContext db,
    ITenantProvider tenant,
    ICurrentUser user,
    IClock clock,
    IUserDirectory users,
    IBackgroundJobs jobs,
    IFileStorage files,
    IValidator<RecordPaymentRequest> validator)
{
    // ------------------------------------------------------------------ collect

    /// <summary>The student, their open dues (oldest first) and any advance credit. Null when not found or not allowed.</summary>
    public async Task<CollectFeeView?> GetCollectViewAsync(Guid studentId, CancellationToken ct = default)
    {
        if (!user.CanManage()) return null;

        var student = await db.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => new
            {
                s.Id, s.AdmissionNo, s.ClassGrade,
                Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName),
                Guardian = s.Guardians.OrderByDescending(g => g.IsPrimary)
                    .Select(g => new { g.Guardian!.FullName, g.Guardian.Phone }).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);
        if (student is null) return null;

        var today = clock.Today();
        var dues = await OpenDuesQuery(studentId)
            .Select(d => new
            {
                d.Id, d.Title, d.DueDate, d.Amount, d.DiscountAmount, d.PaidAmount,
                BatchName = db.Enrollments.Where(e => e.Id == d.EnrollmentId).Select(e => e.Batch!.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var credit = await db.Payments.AsNoTracking()
            .Where(p => p.StudentId == studentId && !p.IsCancelled)
            .SumAsync(p => p.UnallocatedAmount, ct);

        return new CollectFeeView(
            student.Id, student.Name, student.AdmissionNo, student.ClassGrade,
            student.Guardian?.FullName, student.Guardian?.Phone, credit,
            dues.Select(d => new OpenDue(d.Id, d.Title, d.BatchName ?? "-", d.DueDate,
                    d.Amount - d.DiscountAmount - d.PaidAmount, d.DueDate < today))
                .ToList());
    }

    /// <summary>
    /// Records a payment in one transaction: reloads the open dues (with their concurrency tokens), applies the
    /// amount oldest-first (or the manual split), keeps any extra as advance, takes the next receipt number
    /// atomically and saves payment + allocations. Returns the payment id.
    /// </summary>
    public async Task<Result<Guid>> RecordAsync(RecordPaymentRequest request, CancellationToken ct = default)
    {
        if (!user.CanManage() || user.UserId is not { } userId) return Result.Failure<Guid>(Error.Forbidden());

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Result.Invalid<Guid>(validation);

        // Blazor Server keeps one DbContext per circuit: start from fresh data, never from stale tracked dues.
        ClearTracker();

        if (!await db.Students.AnyAsync(s => s.Id == request.StudentId, ct))
            return Result.Failure<Guid>(Error.NotFound("Student"));

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var dues = await db.FeeDues
            .Where(d => d.StudentId == request.StudentId
                        && (d.Status == FeeDueStatus.Pending || d.Status == FeeDueStatus.PartiallyPaid))
            .OrderBy(d => d.DueDate).ThenBy(d => d.Title)
            .ToListAsync(ct);
        var open = dues.Select(d => new AllocatableDue(d.Id, d.DueDate, d.Balance)).ToList();

        AllocationPlan plan;
        if (request.ManualAllocations is { Count: > 0 } manual)
        {
            if (PaymentAllocator.Validate(request.Amount, manual, open) is { } problem)
                return Result.Failure<Guid>(Error.Validation("Allocations", problem));
            plan = new AllocationPlan(manual, request.Amount - manual.Sum(a => a.Amount));
        }
        else
        {
            plan = PaymentAllocator.OldestFirst(request.Amount, open);
        }

        var prefix = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.ReceiptPrefix).FirstAsync(ct);
        var payment = new Payment
        {
            StudentId = request.StudentId,
            PaymentDate = request.PaymentDate,
            Amount = request.Amount,
            UnallocatedAmount = plan.Unallocated,
            Mode = request.Mode,
            ReferenceNo = Clean(request.ReferenceNo),
            Remarks = Clean(request.Remarks),
            ReceivedBy = userId,
            ReceiptNumber = await NextReceiptNumberAsync(request.PaymentDate, prefix, ct),
        };
        db.Payments.Add(payment);

        var byId = dues.ToDictionary(d => d.Id);
        foreach (var allocation in plan.Allocations)
        {
            byId[allocation.FeeDueId].ApplyPayment(allocation.Amount);
            // Explicit Add (not payment.Allocations.Add): keys are set in C#, see EnrollmentService.AddWithDues.
            db.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId = payment.Id,
                FeeDueId = allocation.FeeDueId,
                Amount = allocation.Amount,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            ClearTracker();
            return Result.Failure<Guid>(Error.ConcurrencyConflict);
        }

        // After the commit, so the job always finds the payment. The job sends the WhatsApp/SMS receipt to the
        // primary guardian if they opted in (or SMS is enabled) and skips otherwise.
        jobs.EnqueueReceipt(payment.Id);
        return payment.Id;
    }

    /// <summary>Cancels a payment (Owner only): reverses what it paid on each due and marks it CANCELLED.</summary>
    public async Task<Result> CancelAsync(Guid paymentId, string reason, CancellationToken ct = default)
    {
        if (!user.IsInRole(Roles.Owner) || user.UserId is not { } userId)
            return Result.Failure(Error.Forbidden("Only the institute owner can cancel a payment."));
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(Error.Validation("Reason", "Enter why the payment is being cancelled."));
        if (reason.Length > 300)
            return Result.Failure(Error.Validation("Reason", "The reason can be at most 300 characters."));

        ClearTracker();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null) return Result.Failure(Error.NotFound("Payment"));
        if (payment.IsCancelled) return Result.Success();

        var allocations = await db.PaymentAllocations.Where(a => a.PaymentId == paymentId).ToListAsync(ct);
        var dueIds = allocations.Select(a => a.FeeDueId).ToList();
        var dues = await db.FeeDues.Where(d => dueIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);

        foreach (var allocation in allocations)
        {
            if (dues.TryGetValue(allocation.FeeDueId, out var due)) due.ReversePayment(allocation.Amount);
        }
        payment.Cancel(reason.Trim(), userId, clock.UtcNow);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            ClearTracker();
            return Result.Failure(Error.ConcurrencyConflict);
        }
        return Result.Success();
    }

    // ------------------------------------------------------------------ queries

    public async Task<PaymentList> ListAsync(PaymentQuery query, CancellationToken ct = default)
    {
        if (!user.CanManage()) return new PaymentList([], 0, 0, 1, query.PageSize);

        var payments = db.Payments.AsNoTracking();
        if (query.StudentId is { } studentId) payments = payments.Where(p => p.StudentId == studentId);
        if (query.From is { } from) payments = payments.Where(p => p.PaymentDate >= from);
        if (query.To is { } to) payments = payments.Where(p => p.PaymentDate <= to);
        if (query.Mode is { } mode) payments = payments.Where(p => p.Mode == mode);
        if (!query.IncludeCancelled) payments = payments.Where(p => !p.IsCancelled);

        var rows = payments.Join(db.Students, p => p.StudentId, s => s.Id, (p, s) => new { p, s });
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            rows = rows.Where(x => x.p.ReceiptNumber.ToLower().Contains(term)
                                   || x.s.AdmissionNo.ToLower().Contains(term)
                                   || (x.s.FirstName + " " + (x.s.LastName ?? "")).ToLower().Contains(term)
                                   || (x.p.ReferenceNo != null && x.p.ReferenceNo.ToLower().Contains(term)));
        }

        var total = await rows.CountAsync(ct);
        var collected = await rows.Where(x => !x.p.IsCancelled).SumAsync(x => (decimal?)x.p.Amount, ct) ?? 0;
        var page = Math.Max(1, query.Page);
        var items = await rows
            .OrderByDescending(x => x.p.PaymentDate).ThenByDescending(x => x.p.CreatedAt)
            .Skip((page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new PaymentListItem(
                x.p.Id, x.p.ReceiptNumber, x.p.PaymentDate, x.s.Id,
                x.s.FirstName + (x.s.LastName == null ? "" : " " + x.s.LastName), x.s.AdmissionNo,
                x.p.Amount, x.p.Mode, x.p.ReferenceNo, x.p.IsCancelled))
            .ToListAsync(ct);

        return new PaymentList(items, total, collected, page, query.PageSize);
    }

    public async Task<ReceiptData?> GetReceiptAsync(Guid paymentId, CancellationToken ct = default) =>
        user.CanManage() ? await LoadReceiptAsync(paymentId, ct) : null;

    /// <summary>
    /// The receipt behind a public link sent to a parent (GET /r/{token}). The signed token is the permission, so
    /// there is no role check: only call this after the token was verified and its institute set as the tenant context.
    /// </summary>
    public Task<ReceiptData?> GetReceiptForPublicLinkAsync(Guid paymentId, CancellationToken ct = default) =>
        LoadReceiptAsync(paymentId, ct);

    private async Task<ReceiptData?> LoadReceiptAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null) return null;

        var institute = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == payment.TenantId, ct);
        var student = await db.Students.AsNoTracking()
            .Where(s => s.Id == payment.StudentId)
            .Select(s => new
            {
                s.AdmissionNo, s.ClassGrade,
                Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName),
                Guardian = s.Guardians.OrderByDescending(g => g.IsPrimary)
                    .Select(g => new { g.Guardian!.FullName, g.Guardian.Phone }).FirstOrDefault(),
            })
            .FirstAsync(ct);

        var lines = await db.PaymentAllocations.AsNoTracking()
            .Where(a => a.PaymentId == paymentId)
            .Select(a => new
            {
                a.Amount,
                a.FeeDue!.Title,
                a.FeeDue!.DueDate,
                BatchName = db.Enrollments.Where(e => e.Id == a.FeeDue!.EnrollmentId).Select(e => e.Batch!.Name).FirstOrDefault(),
            })
            .OrderBy(x => x.DueDate)
            .ToListAsync(ct);

        var balanceAfter = await db.FeeDues.AsNoTracking()
            .Where(d => d.StudentId == payment.StudentId
                        && (d.Status == FeeDueStatus.Pending || d.Status == FeeDueStatus.PartiallyPaid))
            .SumAsync(d => (decimal?)(d.Amount - d.DiscountAmount - d.PaidAmount), ct) ?? 0;

        (await users.GetNamesAsync([payment.ReceivedBy], ct)).TryGetValue(payment.ReceivedBy, out var receivedBy);

        var address = string.Join(", ", new[] { institute.AddressLine, institute.City, institute.State, institute.Pincode }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

        var logo = institute.LogoPath is { } logoKey ? await files.ReadAsync(logoKey, ct) : null;

        return new ReceiptData(
            payment.Id, payment.ReceiptNumber, payment.PaymentDate, payment.CreatedAt,
            institute.Name, address.Length > 0 ? address : null, PhoneNumber.Format(institute.Phone), institute.Email, institute.Gstin,
            payment.StudentId, student.Name, student.AdmissionNo, student.ClassGrade,
            student.Guardian?.FullName, student.Guardian?.Phone,
            payment.Amount, AmountInWords.Rupees(payment.Amount), payment.Mode, payment.ReferenceNo, payment.Remarks,
            lines.Select(l => new ReceiptLine(l.Title, l.BatchName ?? "-", l.Amount)).ToList(),
            payment.UnallocatedAmount, balanceAfter, receivedBy,
            payment.IsCancelled, payment.CancelledReason, payment.CancelledAt)
        {
            Logo = logo,
        };
    }

    /// <summary>Today's and this month's collection (non-cancelled), for the dashboard.</summary>
    public async Task<CollectionSummary?> GetCollectionSummaryAsync(CancellationToken ct = default)
    {
        if (!user.CanManage()) return null;

        var today = clock.Today();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var month = await db.Payments.AsNoTracking()
            .Where(p => !p.IsCancelled && p.PaymentDate >= monthStart && p.PaymentDate <= today)
            .Select(p => new { p.PaymentDate, p.Amount })
            .ToListAsync(ct);

        var todays = month.Where(p => p.PaymentDate == today).ToList();
        return new CollectionSummary(todays.Sum(p => p.Amount), month.Sum(p => p.Amount), todays.Count);
    }

    // ------------------------------------------------------------------ helpers

    private IQueryable<FeeDue> OpenDuesQuery(Guid studentId) =>
        db.FeeDues.AsNoTracking()
            .Where(d => d.StudentId == studentId
                        && (d.Status == FeeDueStatus.Pending || d.Status == FeeDueStatus.PartiallyPaid))
            .OrderBy(d => d.DueDate).ThenBy(d => d.Title);

    /// <summary>
    /// Next receipt number for the date's financial year, e.g. ABC/2026-27/00042 (design doc 11.5).
    /// The upsert increments the counter atomically and locks the row until the transaction commits,
    /// so numbers are never duplicated; a rolled-back payment does not use up a number.
    /// </summary>
    private async Task<string> NextReceiptNumberAsync(DateOnly date, string prefix, CancellationToken ct)
    {
        var fy = ReceiptCounter.FinancialYearFor(date);
        var tenantId = tenant.TenantId;

        // ToListAsync (not SingleAsync) so EF runs the SQL as written instead of wrapping it in a sub-query.
        var next = (await db.Database.SqlQuery<int>($"""
            INSERT INTO receipt_counters (tenant_id, financial_year, last_number)
            VALUES ({tenantId}, {fy}, 1)
            ON CONFLICT (tenant_id, financial_year)
            DO UPDATE SET last_number = receipt_counters.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(ct)).Single();

        return FormatReceiptNumber(prefix, fy, next);
    }

    public static string FormatReceiptNumber(string prefix, string financialYear, int number) =>
        $"{prefix}/{financialYear}/{number:D5}";

    private void ClearTracker()
    {
        if (db is DbContext context) context.ChangeTracker.Clear();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
