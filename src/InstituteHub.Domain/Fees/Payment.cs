using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Fees;

public enum PaymentMode { Cash, UPI, Card, BankTransfer, Cheque, Online }

/// <summary>A saved payment is never edited; it is cancelled and re-recorded instead.</summary>
public class Payment : TenantEntity, IAuditable
{
    public Guid StudentId { get; set; }
    /// <summary>Unique per tenant, e.g. ABC/2026-27/00042.</summary>
    public string ReceiptNumber { get; set; } = "";
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Advance kept as credit if more than the open dues.</summary>
    public decimal UnallocatedAmount { get; set; }
    public PaymentMode Mode { get; set; }
    public string? ReferenceNo { get; set; }
    public Guid ReceivedBy { get; set; }
    public string? Remarks { get; set; }
    public bool IsCancelled { get; private set; }
    public string? CancelledReason { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CancelledBy { get; private set; }

    public ICollection<PaymentAllocation> Allocations { get; } = [];

    /// <summary>
    /// Marks the payment cancelled. The caller must reverse each allocation on its FeeDue
    /// in the same transaction (see FeeDue.ReversePayment).
    /// </summary>
    public void Cancel(string reason, Guid cancelledBy, DateTimeOffset at)
    {
        if (IsCancelled) throw new InvalidOperationException("Payment is already cancelled.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required.", nameof(reason));
        IsCancelled = true;
        CancelledReason = reason;
        CancelledBy = cancelledBy;
        CancelledAt = at;
    }
}
