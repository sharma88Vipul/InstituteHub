using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Attendance;
using InstituteHub.Application.Common;
using InstituteHub.Domain.Attendance;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Fees;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Application.Reports;

/// <summary>
/// Reports (design doc 5.1 / 5.3 /reports): collections by date, mode and batch; dues ageing; attendance summary;
/// each with a CSV export. Money reports are for Owner/Staff; teachers see attendance of their own batches only.
/// </summary>
public sealed class ReportService(IAppDbContext db, ICurrentUser user, IClock clock)
{
    public const int LowAttendanceThreshold = 75;

    // ------------------------------------------------------------------ collections

    public async Task<CollectionsReport?> GetCollectionsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!user.CanManage()) return null;
        var (start, end) = Order(from, to);

        var payments = await db.Payments.AsNoTracking()
            .Where(p => !p.IsCancelled && p.PaymentDate >= start && p.PaymentDate <= end)
            .Select(p => new { p.Id, p.PaymentDate, p.Mode, p.Amount, p.UnallocatedAmount })
            .ToListAsync(ct);

        var byBatch = await (
                from a in db.PaymentAllocations
                join p in db.Payments on a.PaymentId equals p.Id
                where !p.IsCancelled && p.PaymentDate >= start && p.PaymentDate <= end
                select new
                {
                    a.Amount,
                    Batch = db.Enrollments.Where(e => e.Id == a.FeeDue!.EnrollmentId).Select(e => e.Batch!.Name).FirstOrDefault(),
                })
            .AsNoTracking()
            .ToListAsync(ct);

        var advance = payments.Sum(p => p.UnallocatedAmount);
        var batches = byBatch
            .GroupBy(x => x.Batch ?? "(no batch)")
            .Select(g => new CollectionByBatch(g.Key, g.Sum(x => x.Amount)))
            .OrderByDescending(x => x.Amount)
            .ToList();
        if (advance > 0) batches.Add(new CollectionByBatch("Advance (not yet applied to a fee)", advance));

        return new CollectionsReport(
            start, end,
            payments.Sum(p => p.Amount),
            payments.Count,
            advance,
            payments.GroupBy(p => p.PaymentDate)
                .Select(g => new CollectionByDay(g.Key, g.Count(), g.Sum(p => p.Amount)))
                .OrderBy(x => x.Date).ToList(),
            payments.GroupBy(p => p.Mode)
                .Select(g => new CollectionByMode(g.Key, g.Count(), g.Sum(p => p.Amount)))
                .OrderByDescending(x => x.Amount).ToList(),
            batches);
    }

    /// <summary>Every receipt in the period (cancelled ones marked), for accountants.</summary>
    public async Task<byte[]?> CollectionsCsvAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!user.CanManage()) return null;
        var (start, end) = Order(from, to);

        var rows = await (
                from p in db.Payments
                join s in db.Students on p.StudentId equals s.Id
                where p.PaymentDate >= start && p.PaymentDate <= end
                orderby p.PaymentDate, p.ReceiptNumber
                select new
                {
                    p.PaymentDate, p.ReceiptNumber, s.AdmissionNo,
                    Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName),
                    p.Mode, p.ReferenceNo, p.Amount, p.UnallocatedAmount, p.IsCancelled, p.CancelledReason,
                })
            .AsNoTracking()
            .ToListAsync(ct);

        return Csv.Write(
            ["Date", "Receipt no", "Admission no", "Student", "Mode", "Reference", "Amount", "Advance", "Status", "Cancel reason"],
            rows.Select(r => new object?[]
            {
                r.PaymentDate, r.ReceiptNumber, r.AdmissionNo, r.Name, r.Mode, r.ReferenceNo,
                r.Amount, r.UnallocatedAmount, r.IsCancelled ? "Cancelled" : "Paid", r.CancelledReason,
            }));
    }

    // ------------------------------------------------------------------ dues ageing

    public async Task<DuesAgeingReport?> GetDuesAgeingAsync(CancellationToken ct = default)
    {
        if (!user.CanManage()) return null;
        var today = clock.Today();

        var dues = await db.FeeDues.AsNoTracking()
            .Where(d => d.Status == FeeDueStatus.Pending || d.Status == FeeDueStatus.PartiallyPaid)
            .Select(d => new { d.StudentId, d.DueDate, Balance = d.Amount - d.DiscountAmount - d.PaidAmount })
            .ToListAsync(ct);
        dues = dues.Where(d => d.Balance > 0).ToList();

        var buckets = DuesAgeing.Labels
            .Select((label, i) =>
            {
                var inBucket = dues.Where(d => DuesAgeing.Bucket(d.DueDate, today) == i).ToList();
                return new AgeingBucket(label, inBucket.Count, inBucket.Select(d => d.StudentId).Distinct().Count(), inBucket.Sum(d => d.Balance));
            })
            .ToList();

        var studentIds = dues.Select(d => d.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new
            {
                s.Id, s.AdmissionNo,
                Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName),
                Guardian = s.Guardians.OrderByDescending(g => g.IsPrimary)
                    .Select(g => new { g.Guardian!.FullName, g.Guardian.Phone }).FirstOrDefault(),
            })
            .ToDictionaryAsync(s => s.Id, ct);

        var rows = dues
            .GroupBy(d => d.StudentId)
            .Where(g => students.ContainsKey(g.Key))
            .Select(g =>
            {
                var s = students[g.Key];
                var oldest = g.Min(d => d.DueDate);
                return new StudentDueRow(
                    s.Id, s.Name, s.AdmissionNo, s.Guardian?.FullName, s.Guardian?.Phone,
                    g.Sum(d => d.Balance),
                    g.Where(d => d.DueDate < today).Sum(d => d.Balance),
                    oldest,
                    Math.Max(0, today.DayNumber - oldest.DayNumber));
            })
            .OrderByDescending(r => r.DaysOverdue).ThenByDescending(r => r.Balance)
            .ToList();

        return new DuesAgeingReport(today, dues.Sum(d => d.Balance), dues.Where(d => d.DueDate < today).Sum(d => d.Balance), buckets, rows);
    }

    public async Task<byte[]?> DuesCsvAsync(CancellationToken ct = default)
    {
        if (await GetDuesAgeingAsync(ct) is not { } report) return null;
        return Csv.Write(
            ["Admission no", "Student", "Guardian", "Guardian phone", "Total due", "Overdue", "Oldest due date", "Days late"],
            report.Students.Select(r => new object?[]
            {
                r.AdmissionNo, r.StudentName, r.GuardianName, r.GuardianPhone, r.Balance, r.Overdue, r.OldestDueDate, r.DaysOverdue,
            }));
    }

    // ------------------------------------------------------------------ attendance

    public async Task<AttendanceReport?> GetAttendanceAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!user.CanManage() && !user.IsInRole(Roles.Teacher)) return null;
        var (start, end) = Order(from, to);

        var marks = await MarksAsync(start, end, ct);

        var batches = marks
            .GroupBy(m => new { m.BatchId, m.BatchName })
            .Select(g => new BatchAttendanceRow(
                g.Key.BatchId, g.Key.BatchName,
                g.Select(m => m.SessionId).Distinct().Count(),
                g.Select(m => m.StudentId).Distinct().Count(),
                AttendanceSummary.From(g.Select(m => m.Status))))
            .OrderBy(b => b.Batch)
            .ToList();

        var low = (await StudentRowsAsync(marks, ct))
            .Where(r => r.Summary.Percentage is { } pct && pct < LowAttendanceThreshold)
            .OrderBy(r => r.Summary.Percentage).ThenBy(r => r.StudentName)
            .ToList();

        return new AttendanceReport(start, end, LowAttendanceThreshold, batches, low);
    }

    public async Task<byte[]?> AttendanceCsvAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!user.CanManage() && !user.IsInRole(Roles.Teacher)) return null;
        var (start, end) = Order(from, to);

        var rows = await StudentRowsAsync(await MarksAsync(start, end, ct), ct);
        return Csv.Write(
            ["Batch", "Admission no", "Student", "Present", "Late", "Absent", "Leave", "Attendance %"],
            rows.OrderBy(r => r.Batch).ThenBy(r => r.StudentName).Select(r => new object?[]
            {
                r.Batch, r.AdmissionNo, r.StudentName, r.Summary.Present, r.Summary.Late, r.Summary.Absent, r.Summary.Leave,
                r.Summary.Percentage,
            }));
    }

    private sealed record Mark(Guid SessionId, Guid BatchId, string BatchName, Guid StudentId, AttendanceStatus Status);

    private async Task<List<Mark>> MarksAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var batches = db.Batches.AsNoTracking();
        if (user.IsTeacherOnly())
        {
            var me = user.UserId;
            batches = batches.Where(b => b.TeacherId == me);
        }

        var rows = await (
                from s in db.AttendanceSessions
                from r in s.Records
                join b in batches on s.BatchId equals b.Id
                where s.SessionDate >= start && s.SessionDate <= end
                select new { SessionId = s.Id, s.BatchId, BatchName = b.Name, r.StudentId, r.Status })
            .AsNoTracking()
            .ToListAsync(ct);

        return rows.Select(r => new Mark(r.SessionId, r.BatchId, r.BatchName, r.StudentId, r.Status)).ToList();
    }

    private async Task<List<StudentAttendanceRow>> StudentRowsAsync(List<Mark> marks, CancellationToken ct)
    {
        var studentIds = marks.Select(m => m.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.AdmissionNo, Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName) })
            .ToDictionaryAsync(s => s.Id, ct);

        return marks
            .GroupBy(m => new { m.StudentId, m.BatchName })
            .Select(g => new StudentAttendanceRow(
                g.Key.StudentId,
                students.TryGetValue(g.Key.StudentId, out var s) ? s.Name : "(removed student)",
                s?.AdmissionNo ?? "",
                g.Key.BatchName,
                AttendanceSummary.From(g.Select(m => m.Status))))
            .ToList();
    }

    private static (DateOnly Start, DateOnly End) Order(DateOnly a, DateOnly b) => a <= b ? (a, b) : (b, a);
}
