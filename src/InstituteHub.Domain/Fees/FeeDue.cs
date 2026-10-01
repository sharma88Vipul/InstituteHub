using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Fees;

public enum FeeDueStatus { Pending, PartiallyPaid, Paid, Waived }

/// <summary>One amount a student owes: one per instalment or per month.</summary>
public class FeeDue : TenantEntity, IAuditable
{
    public Guid EnrollmentId { get; set; }
    /// <summary>Denormalised for fast dues lists.</summary>
    public Guid StudentId { get; set; }
    public string Title { get; set; } = "";
    /// <summary>e.g. 2026-10 for monthly dues; prevents duplicates.</summary>
    public string? PeriodKey { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal PaidAmount { get; private set; }
    public FeeDueStatus Status { get; private set; } = FeeDueStatus.Pending;
    public DateTimeOffset? LastReminderAt { get; set; }
    public short ReminderCount { get; set; }

    /// <summary>Calculated, not stored.</summary>
    public decimal Balance => Amount - DiscountAmount - PaidAmount;

    public void ApplyPayment(decimal amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        if (Status == FeeDueStatus.Waived) throw new InvalidOperationException("Cannot pay a waived due.");
        if (amount > Balance) throw new InvalidOperationException("Amount exceeds balance.");
        PaidAmount += amount;
        Status = Balance == 0 ? FeeDueStatus.Paid : FeeDueStatus.PartiallyPaid;
    }

    public void ReversePayment(decimal amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be positive.");
        if (amount > PaidAmount) throw new InvalidOperationException("Cannot reverse more than was paid.");
        PaidAmount -= amount;
        Status = PaidAmount == 0 ? FeeDueStatus.Pending : FeeDueStatus.PartiallyPaid;
    }

    public void Waive()
    {
        if (PaidAmount > 0) throw new InvalidOperationException("Cannot waive a due that has payments.");
        Status = FeeDueStatus.Waived;
    }
}
