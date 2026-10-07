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
