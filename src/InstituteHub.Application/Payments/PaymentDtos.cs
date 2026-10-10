using FluentValidation;
using InstituteHub.Application.Abstractions;
using InstituteHub.Domain.Fees;

namespace InstituteHub.Application.Payments;

public sealed record RecordPaymentRequest(
    Guid StudentId,
    DateOnly PaymentDate,
    decimal Amount,
    PaymentMode Mode,
    string? ReferenceNo,
    string? Remarks,
    IReadOnlyList<Allocation>? ManualAllocations = null);

public sealed record OpenDue(Guid Id, string Title, string BatchName, DateOnly DueDate, decimal Balance, bool IsOverdue);

/// <summary>Everything the Collect fee page needs about one student.</summary>
public sealed record CollectFeeView(
    Guid StudentId,
    string StudentName,
    string AdmissionNo,
    string? ClassGrade,
    string? GuardianName,
    string? GuardianPhone,
    decimal AdvanceCredit,
    IReadOnlyList<OpenDue> Dues)
{
    public decimal TotalBalance => Dues.Sum(d => d.Balance);
    public decimal OverdueBalance => Dues.Where(d => d.IsOverdue).Sum(d => d.Balance);
}

public sealed record PaymentQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    PaymentMode? Mode = null,
    string? Search = null,
    bool IncludeCancelled = true,
    Guid? StudentId = null,
    int Page = 1,
    int PageSize = 50);

public sealed record PaymentListItem(
    Guid Id,
    string ReceiptNumber,
    DateOnly PaymentDate,
    Guid StudentId,
    string StudentName,
    string AdmissionNo,
    decimal Amount,
    PaymentMode Mode,
    string? ReferenceNo,
    bool IsCancelled);

public sealed record PaymentList(IReadOnlyList<PaymentListItem> Items, int TotalCount, decimal TotalCollected, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record ReceiptLine(string Title, string BatchName, decimal Amount);

/// <summary>Data printed on a receipt (HTML view and PDF).</summary>
public sealed record ReceiptData(
    Guid PaymentId,
    string ReceiptNumber,
    DateOnly PaymentDate,
    DateTimeOffset RecordedAt,
    string InstituteName,
    string? InstituteAddress,
    string InstitutePhone,
    string? InstituteEmail,
    string? Gstin,
    Guid StudentId,
    string StudentName,
    string AdmissionNo,
    string? ClassGrade,
    string? GuardianName,
    string? GuardianPhone,
    decimal Amount,
    string AmountInWords,
    PaymentMode Mode,
    string? ReferenceNo,
    string? Remarks,
    IReadOnlyList<ReceiptLine> Lines,
    decimal Unallocated,
    decimal BalanceAfter,
    string? ReceivedByName,
    bool IsCancelled,
    string? CancelledReason,
    DateTimeOffset? CancelledAt)
{
    /// <summary>Institute logo (PNG/JPEG) for the PDF; null when none is uploaded.</summary>
    public byte[]? Logo { get; init; }
}

public sealed record CollectionSummary(decimal Today, decimal ThisMonth, int PaymentsToday);

public sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator(IClock clock)
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Enter the amount received.")
            .LessThanOrEqualTo(10_000_000).WithMessage("That amount looks too large. Please check it.");
        RuleFor(x => x.PaymentDate).LessThanOrEqualTo(_ => clock.Today()).WithMessage("The payment date cannot be in the future.");
        RuleFor(x => x.ReferenceNo).MaximumLength(60);
        RuleFor(x => x.ReferenceNo).NotEmpty()
            .When(x => x.Mode is PaymentMode.Cheque or PaymentMode.BankTransfer)
            .WithMessage("Enter the cheque or bank transfer reference number.");
        RuleFor(x => x.Remarks).MaximumLength(300);
    }
}
