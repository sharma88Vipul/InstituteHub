using InstituteHub.Domain.Fees;

namespace InstituteHub.Application.Fees;

public sealed record FeeDueItem(
    Guid Id,
    string Title,
    string BatchName,
    DateOnly DueDate,
    decimal Amount,
    decimal DiscountAmount,
    decimal PaidAmount,
    FeeDueStatus Status,
    bool IsOverdue)
{
    public decimal Balance => Status == FeeDueStatus.Waived ? 0 : Amount - DiscountAmount - PaidAmount;
}

public sealed record FeeTotals(decimal Gross, decimal Discount, decimal Paid, decimal Balance, decimal Overdue);

public sealed record StudentFees(FeeTotals Totals, IReadOnlyList<FeeDueItem> Dues);

public sealed record DueGenerationResult(int EnrollmentsChecked, int DuesCreated);

public enum DueFilter { Overdue, DueThisWeek, AllPending }

public sealed record PendingDuesQuery(
    DueFilter Filter = DueFilter.Overdue,
    Guid? BatchId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record PendingDueRow(
    Guid DueId,
    Guid StudentId,
    string StudentName,
    string AdmissionNo,
    string? GuardianPhone,
    string BatchName,
    string Title,
    DateOnly DueDate,
    decimal Balance,
    int DaysOverdue,
    short ReminderCount);

public sealed record PendingDues(
    IReadOnlyList<PendingDueRow> Rows,
    int TotalCount,
    decimal TotalBalance,
    int StudentCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
