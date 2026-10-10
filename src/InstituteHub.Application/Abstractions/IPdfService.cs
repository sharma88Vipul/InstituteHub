using InstituteHub.Application.Payments;

namespace InstituteHub.Application.Abstractions;

/// <summary>PDF documents (QuestPDF implementation in Infrastructure/Pdf).</summary>
public interface IPdfService
{
    /// <summary>A5 fee receipt for printing or sharing.</summary>
    byte[] RenderReceipt(ReceiptData receipt);
}
