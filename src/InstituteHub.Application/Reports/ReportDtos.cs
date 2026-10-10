using InstituteHub.Application.Attendance;
using InstituteHub.Domain.Fees;

namespace InstituteHub.Application.Reports;

// ------------------------------------------------------------------ collections

public sealed record CollectionByDay(DateOnly Date, int Receipts, decimal Amount);

public sealed record CollectionByMode(PaymentMode Mode, int Receipts, decimal Amount);

public sealed record CollectionByBatch(string Batch, decimal Amount);

public sealed record CollectionsReport(
    DateOnly From,
    DateOnly To,
    decimal Total,
    int Receipts,
    decimal Advance,
    IReadOnlyList<CollectionByDay> ByDay,
    IReadOnlyList<CollectionByMode> ByMode,
    IReadOnlyList<CollectionByBatch> ByBatch);

// ------------------------------------------------------------------ dues ageing

public sealed record AgeingBucket(string Label, int Dues, int Students, decimal Amount);

public sealed record StudentDueRow(
    Guid StudentId,
    string StudentName,
    string AdmissionNo,
    string? GuardianName,
    string? GuardianPhone,
    decimal Balance,
    decimal Overdue,
    DateOnly OldestDueDate,
    int DaysOverdue);

public sealed record DuesAgeingReport(
    DateOnly AsOf,
    decimal Total,
    decimal OverdueTotal,
    IReadOnlyList<AgeingBucket> Buckets,
    IReadOnlyList<StudentDueRow> Students);

/// <summary>Ageing buckets for unpaid dues by days past the due date.</summary>
public static class DuesAgeing
{
    public static readonly IReadOnlyList<string> Labels = ["Not due yet", "1–30 days late", "31–60 days late", "61–90 days late", "Over 90 days late"];

    public static int Bucket(DateOnly dueDate, DateOnly today)
    {
        var late = today.DayNumber - dueDate.DayNumber;
        return late switch
        {
            <= 0 => 0,
            <= 30 => 1,
            <= 60 => 2,
            <= 90 => 3,
            _ => 4,
        };
    }
}

// ------------------------------------------------------------------ attendance

public sealed record BatchAttendanceRow(Guid BatchId, string Batch, int Sessions, int Students, AttendanceSummary Summary);

public sealed record StudentAttendanceRow(
    Guid StudentId, string StudentName, string AdmissionNo, string Batch, AttendanceSummary Summary);

public sealed record AttendanceReport(
    DateOnly From,
    DateOnly To,
    int Threshold,
    IReadOnlyList<BatchAttendanceRow> Batches,
    IReadOnlyList<StudentAttendanceRow> LowAttendance);
