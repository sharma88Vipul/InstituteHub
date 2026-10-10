using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Batches;
using InstituteHub.Application.Common;
using InstituteHub.Application.Users;
using InstituteHub.Domain.Attendance;
using InstituteHub.Domain.Batches;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Application.Attendance;

/// <summary>
/// Daily attendance per batch (design doc 6.5): one session per batch per date, one record per student.
/// Owner/Staff can mark any batch at any time; a Teacher only their own batches and only for the last
/// <see cref="TeacherEditDays"/> days.
/// </summary>
public sealed class AttendanceService(IAppDbContext db, ICurrentUser user, IClock clock, IUserDirectory users, IBackgroundJobs jobs)
{
    public const int TeacherEditDays = 7;

    private IQueryable<Batch> VisibleBatches()
    {
        var batches = db.Batches.AsNoTracking();
        if (user.IsTeacherOnly())
        {
            var me = user.UserId;
            batches = batches.Where(b => b.TeacherId == me);
        }
        return batches;
    }

    private bool CanMark => user.CanManage() || user.IsInRole(Roles.Teacher);

    /// <summary>Null when the date is editable, otherwise the reason (shown on the page).</summary>
    private string? EditBlockedReason(DateOnly date)
    {
        if (!CanMark) return "You do not have permission to mark attendance.";
        var today = clock.Today();
        if (date > today) return "Attendance cannot be marked for a future date.";
        if (user.IsTeacherOnly() && today.DayNumber - date.DayNumber > TeacherEditDays)
            return $"Teachers can change attendance only for the last {TeacherEditDays} days. Ask the office to correct older dates.";
        return null;
    }

    // ------------------------------------------------------------------ marking

    /// <summary>Active batches the user can mark, with whether they run today and are already marked.</summary>
    public async Task<IReadOnlyList<TodayBatch>> GetTodayAsync(DateOnly? date = null, CancellationToken ct = default)
    {
        var day = date ?? clock.Today();
        var batches = await VisibleBatches()
            .Where(b => b.IsActive)
            .OrderBy(b => b.StartTime).ThenBy(b => b.Name)
            .Select(b => new
            {
                b.Id, b.Name, b.DaysOfWeek, b.StartTime, b.EndTime,
                Marked = db.AttendanceSessions.Any(s => s.BatchId == b.Id && s.SessionDate == day),
            })
            .ToListAsync(ct);

        var flag = DayFlag(day);
        return batches
            .Select(b => new TodayBatch(b.Id, b.Name, BatchSchedule.Describe(b.DaysOfWeek, b.StartTime, b.EndTime),
                b.DaysOfWeek.HasFlag(flag), b.Marked))
            .OrderByDescending(b => b.ScheduledToday)
            .ToList();
    }

    /// <summary>
    /// Students with an active enrollment on that date (all Present by default), plus anyone already
    /// marked in an existing register. Null when the batch is not visible to the user.
    /// </summary>
    public async Task<AttendanceSheet?> GetSheetAsync(Guid batchId, DateOnly date, CancellationToken ct = default)
    {
        var batch = await VisibleBatches().FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null) return null;

        var session = await db.AttendanceSessions.AsNoTracking()
            .Include(s => s.Records)
            .FirstOrDefaultAsync(s => s.BatchId == batchId && s.SessionDate == date, ct);

        var enrolledIds = await EnrolledStudentIdsAsync(batchId, date, ct);
        var markedIds = session?.Records.Select(r => r.StudentId).ToList() ?? [];
        var studentIds = enrolledIds.Union(markedIds).ToList();

        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .OrderBy(s => s.FirstName).ThenBy(s => s.LastName)
            .Select(s => new { s.Id, s.AdmissionNo, Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName) })
            .ToListAsync(ct);

        var records = session?.Records.ToDictionary(r => r.StudentId) ?? new Dictionary<Guid, AttendanceRecord>();
        var rows = students
            .Select(s => records.TryGetValue(s.Id, out var r)
                ? new AttendanceSheetRow(s.Id, s.AdmissionNo, s.Name, r.Status, r.Remark)
                : new AttendanceSheetRow(s.Id, s.AdmissionNo, s.Name, AttendanceStatus.Present, null))
            .ToList();

        string? markedBy = null;
        if (session is not null)
        {
            (await users.GetNamesAsync([session.MarkedBy], ct)).TryGetValue(session.MarkedBy, out markedBy);
        }

        var blocked = EditBlockedReason(date);
        return new AttendanceSheet(
            batch.Id, batch.Name, date,
            IsScheduledDay: batch.DaysOfWeek.HasFlag(DayFlag(date)),
            AlreadyMarked: session is not null,
            CanEdit: blocked is null,
            ReadOnlyReason: blocked,
            MarkedAt: session?.MarkedAt,
            MarkedByName: markedBy,
            Rows: rows);
    }

    /// <summary>Creates or updates the register (upsert on batch + date) and its records.</summary>
    public async Task<Result<AttendanceSummary>> SaveAsync(SaveAttendanceRequest request, CancellationToken ct = default)
    {
        if (user.UserId is not { } userId) return Result.Failure<AttendanceSummary>(Error.Forbidden());
        if (EditBlockedReason(request.Date) is { } reason)
            return Result.Failure<AttendanceSummary>(Error.Validation("Date", reason));

        var batch = await VisibleBatches().FirstOrDefaultAsync(b => b.Id == request.BatchId, ct);
        if (batch is null) return Result.Failure<AttendanceSummary>(Error.NotFound("Batch"));

        if (request.Marks.Count == 0)
            return Result.Failure<AttendanceSummary>(Error.Validation("Marks", "There are no students to mark."));
        if (request.Marks.GroupBy(m => m.StudentId).Any(g => g.Count() > 1))
            return Result.Failure<AttendanceSummary>(Error.Validation("Marks", "A student is listed twice."));
        if (request.Marks.Any(m => m.Remark is { Length: > 200 }))
            return Result.Failure<AttendanceSummary>(Error.Validation("Remark", "Remarks can be at most 200 characters."));

        var session = await db.AttendanceSessions
            .Include(s => s.Records)
            .FirstOrDefaultAsync(s => s.BatchId == request.BatchId && s.SessionDate == request.Date, ct);

        // Only students enrolled on that date (or already in this register) can be marked.
        var allowed = (await EnrolledStudentIdsAsync(request.BatchId, request.Date, ct)).ToHashSet();
        if (session is not null) allowed.UnionWith(session.Records.Select(r => r.StudentId));
        if (request.Marks.Any(m => !allowed.Contains(m.StudentId)))
            return Result.Failure<AttendanceSummary>(Error.Validation("Marks", "Some students are not in this batch on this date. Reload and try again."));

        var now = clock.UtcNow;
        if (session is null)
        {
            session = new AttendanceSession { BatchId = request.BatchId, SessionDate = request.Date };
            db.AttendanceSessions.Add(session);
        }
        session.MarkedBy = userId;
        session.MarkedAt = now;

        var existing = session.Records.ToDictionary(r => r.StudentId);
        foreach (var mark in request.Marks)
        {
            var remark = string.IsNullOrWhiteSpace(mark.Remark) ? null : mark.Remark.Trim();
            if (existing.TryGetValue(mark.StudentId, out var record))
            {
                record.Status = mark.Status;
                record.Remark = remark;
            }
            else
            {
                // Add through the DbSet, not session.Records: our keys are set in C# (UUID v7), so EF would
                // otherwise treat a record found via the navigation as an existing row and try to UPDATE it.
                var newRecord = new AttendanceRecord { SessionId = session.Id, StudentId = mark.StudentId, Status = mark.Status, Remark = remark };
                db.AttendanceRecords.Add(newRecord);
                session.Records.Add(newRecord);
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Most likely two people saved the same register at the same moment (unique batch + date).
            if (db is DbContext context) context.ChangeTracker.Clear();
            return Result.Failure<AttendanceSummary>(Error.Conflict(
                "Attendance.Conflict", "Someone else just saved this register. Reload the page and try again."));
        }

        // Absent alerts to guardians (AbsentAlertJob): only for today's register; the job skips students
        // already alerted, so saving the register again does not message a parent twice.
        if (request.Date == clock.Today() && request.Marks.Any(m => m.Status == AttendanceStatus.Absent))
        {
            jobs.EnqueueAbsentAlerts(session.Id);
        }

        return AttendanceSummary.From(session.Records.Select(r => r.Status));
    }

    // ------------------------------------------------------------------ history

    /// <summary>A student's attendance across their batches (default: last 90 days).</summary>
    public async Task<StudentAttendance> GetStudentAttendanceAsync(
        Guid studentId, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var end = to ?? clock.Today();
        var start = from ?? end.AddDays(-90);
        var visibleBatchIds = VisibleBatches().Select(b => b.Id);

        var rows = await (
                from s in db.AttendanceSessions
                from r in s.Records
                join b in db.Batches on s.BatchId equals b.Id
                where r.StudentId == studentId && s.SessionDate >= start && s.SessionDate <= end
                      && visibleBatchIds.Contains(s.BatchId)
                orderby s.SessionDate descending, b.Name
                select new StudentAttendanceEntry(s.SessionDate, b.Id, b.Name, r.Status, r.Remark))
            .AsNoTracking()
            .ToListAsync(ct);

        return new StudentAttendance(AttendanceSummary.From(rows.Select(r => r.Status)), rows.Take(30).ToList());
    }

    /// <summary>Registers and per-student attendance for a batch in a date range (default: this month).</summary>
    public async Task<BatchAttendance?> GetBatchAttendanceAsync(
        Guid batchId, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        if (!await VisibleBatches().AnyAsync(b => b.Id == batchId, ct)) return null;

        var today = clock.Today();
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? today;

        var marks = await (
                from s in db.AttendanceSessions
                from r in s.Records
                where s.BatchId == batchId && s.SessionDate >= start && s.SessionDate <= end
                select new { s.SessionDate, r.StudentId, r.Status })
            .AsNoTracking()
            .ToListAsync(ct);

        var sessions = marks
            .GroupBy(m => m.SessionDate)
            .OrderByDescending(g => g.Key)
            .Select(g => new BatchAttendanceSession(g.Key, AttendanceSummary.From(g.Select(x => x.Status))))
            .ToList();

        var studentIds = marks.Select(m => m.StudentId).Distinct().ToList();
        var names = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName) })
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var students = marks
            .GroupBy(m => m.StudentId)
            .Select(g => new BatchStudentAttendance(
                g.Key, names.TryGetValue(g.Key, out var n) ? n : "(removed student)",
                AttendanceSummary.From(g.Select(x => x.Status))))
            .OrderBy(s => s.Summary.Percentage ?? 101).ThenBy(s => s.FullName)
            .ToList();

        return new BatchAttendance(start, end, sessions, students);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<List<Guid>> EnrolledStudentIdsAsync(Guid batchId, DateOnly date, CancellationToken ct) =>
        await db.Enrollments.AsNoTracking()
            .Where(e => e.BatchId == batchId && e.EnrolledOn <= date && (e.EndedOn == null || e.EndedOn >= date))
            .Select(e => e.StudentId)
            .Distinct()
            .ToListAsync(ct);

    private static WeekDays DayFlag(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => WeekDays.Monday,
        DayOfWeek.Tuesday => WeekDays.Tuesday,
        DayOfWeek.Wednesday => WeekDays.Wednesday,
        DayOfWeek.Thursday => WeekDays.Thursday,
        DayOfWeek.Friday => WeekDays.Friday,
        DayOfWeek.Saturday => WeekDays.Saturday,
        _ => WeekDays.Sunday,
    };
}
