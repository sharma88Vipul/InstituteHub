namespace InstituteHub.Application.Abstractions;

/// <summary>Receipt and report PDFs (QuestPDF implementation arrives in week 6).</summary>
public interface IPdfService
{
    Task<byte[]> RenderReceiptAsync(Guid paymentId, CancellationToken ct);
}
