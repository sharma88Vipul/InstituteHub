using InstituteHub.Domain.Attendance;

namespace InstituteHub.Application.Attendance;

/// <summary>The register for one batch on one date, ready to mark.</summary>
public sealed record AttendanceSheet(
    Guid BatchId,
    string BatchName,
    DateOnly Date,
    bool IsScheduledDay,
    bool AlreadyMarked,
    bool CanEdit,
    string? ReadOnlyReason,
    DateTimeOffset? MarkedAt,
    string? MarkedByName,
    IReadOnlyList<AttendanceSheetRow> Rows);

public sealed record AttendanceSheetRow(
    Guid StudentId,
    string AdmissionNo,
    string FullName,
    AttendanceStatus Status,
    string? Remark);

public sealed record AttendanceMark(Guid StudentId, AttendanceStatus Status, string? Remark = null);

public sealed record SaveAttendanceRequest(Guid BatchId, DateOnly Date, IReadOnlyList<AttendanceMark> Marks);

/// <summary>Counts for a period. Late counts as attended; Leave is excluded from the percentage.</summary>
public sealed record AttendanceSummary(int Present, int Absent, int Late, int Leave)
{
    public static readonly AttendanceSummary Empty = new(0, 0, 0, 0);

    public int Total => Present + Absent + Late + Leave;

    /// <summary>(Present + Late) ÷ (all marks except Leave), or null when nothing to count.</summary>
    public double? Percentage
    {
        get
        {
            var counted = Present + Absent + Late;
            return counted == 0 ? null : Math.Round((Present + Late) * 100.0 / counted, 1);
        }
    }

    public static AttendanceSummary From(IEnumerable<AttendanceStatus> statuses)
    {
        int p = 0, a = 0, l = 0, v = 0;
        foreach (var s in statuses)
        {
            switch (s)
            {
                case AttendanceStatus.Present: p++; break;
                case AttendanceStatus.Absent: a++; break;
                case AttendanceStatus.Late: l++; break;
                case AttendanceStatus.Leave: v++; break;
            }
        }
        return new AttendanceSummary(p, a, l, v);
    }
}

public sealed record StudentAttendanceEntry(DateOnly Date, Guid BatchId, string BatchName, AttendanceStatus Status, string? Remark);

public sealed record StudentAttendance(AttendanceSummary Summary, IReadOnlyList<StudentAttendanceEntry> Recent);

public sealed record BatchAttendanceSession(DateOnly Date, AttendanceSummary Counts);

public sealed record BatchStudentAttendance(Guid StudentId, string FullName, AttendanceSummary Summary);

public sealed record BatchAttendance(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<BatchAttendanceSession> Sessions,
    IReadOnlyList<BatchStudentAttendance> Students);

/// <summary>A batch on the attendance start screen.</summary>
public sealed record TodayBatch(Guid BatchId, string Name, string Schedule, bool ScheduledToday, bool MarkedToday);
