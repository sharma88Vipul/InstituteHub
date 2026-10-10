using System.Globalization;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Payments;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace InstituteHub.Infrastructure.Pdf;

/// <summary>
/// Receipt PDFs with QuestPDF. The Community licence is free for organisations with less than USD 1M annual
/// revenue – check the QuestPDF licence terms before selling the product at scale (design doc 1.2).
/// </summary>
public sealed class QuestPdfService : IPdfService
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    static QuestPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] RenderReceipt(ReceiptData r)
    {
        var logo = LoadLogo(r.Logo);
        return RenderReceipt(r, logo);
    }

    /// <summary>A logo QuestPDF cannot read is left out rather than breaking every receipt.</summary>
    private static Image? LoadLogo(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            return Image.FromBinaryData(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] RenderReceipt(ReceiptData r, Image? logo) => Document.Create(document => document.Page(page =>
    {
        page.Size(PageSizes.A5);
        page.Margin(24);
        page.DefaultTextStyle(t => t.FontSize(10));

        page.Header().Column(col =>
        {
            col.Item().Row(top =>
            {
                if (logo is not null)
                {
                    // FitArea keeps the logo's proportions inside a 48 x 48 pt box.
                    top.ConstantItem(48).Height(48).Image(logo).FitArea();
                    top.ConstantItem(10);
                }
                top.RelativeItem().Column(institute =>
                {
                    institute.Item().Text(r.InstituteName).FontSize(16).Bold();
                    if (r.InstituteAddress is { } address) institute.Item().Text(address);
                    institute.Item().Text("Phone: " + r.InstitutePhone + (r.InstituteEmail is { } email ? "   ·   " + email : ""));
                    if (r.Gstin is { } gstin) institute.Item().Text("GSTIN: " + gstin);
                });
            });
            col.Item().PaddingVertical(6).LineHorizontal(1);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text("FEE RECEIPT").FontSize(13).Bold();
                row.RelativeItem().AlignRight().Text("No. " + r.ReceiptNumber).Bold();
            });
            col.Item().AlignRight().Text("Date: " + r.PaymentDate.ToString("dd MMM yyyy", India));
        });

        page.Content().PaddingVertical(10).Column(col =>
        {
            col.Spacing(5);

            if (r.IsCancelled)
            {
                col.Item().Background(Colors.Red.Lighten4).Padding(6)
                    .Text("CANCELLED" + (r.CancelledAt is { } at ? " on " + at.ToOffset(TimeSpan.FromHours(5.5)).ToString("dd MMM yyyy", India) : "")
                          + (r.CancelledReason is { } reason ? " – " + reason : ""))
                    .FontColor(Colors.Red.Darken3).Bold();
            }

            col.Item().Text(t =>
            {
                t.Span("Received from: ").SemiBold();
                t.Span(r.StudentName + "  (Adm. no. " + r.AdmissionNo + (r.ClassGrade is { } grade ? ", " + grade : "") + ")");
            });
            if (r.GuardianName is { } guardian)
            {
                col.Item().Text(t =>
                {
                    t.Span("Parent / guardian: ").SemiBold();
                    t.Span(guardian);
                });
            }

            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4);
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                });

                table.Header(h =>
                {
                    h.Cell().Element(HeaderCell).Text("Towards");
                    h.Cell().Element(HeaderCell).Text("Batch");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Amount");
                });

                foreach (var line in r.Lines)
                {
                    table.Cell().Element(BodyCell).Text(line.Title);
                    table.Cell().Element(BodyCell).Text(line.BatchName);
                    table.Cell().Element(BodyCell).AlignRight().Text(Rs(line.Amount));
                }

                if (r.Unallocated > 0)
                {
                    table.Cell().Element(BodyCell).Text("Advance (adjusted against future fees)");
                    table.Cell().Element(BodyCell).Text("");
                    table.Cell().Element(BodyCell).AlignRight().Text(Rs(r.Unallocated));
                }

                table.Cell().ColumnSpan(2).PaddingTop(4).Text("Total received").Bold();
                table.Cell().PaddingTop(4).AlignRight().Text(Rs(r.Amount)).Bold();
            });

            col.Item().Text(r.AmountInWords).Italic();
            col.Item().Text(t =>
            {
                t.Span("Payment mode: ").SemiBold();
                t.Span(r.Mode.ToString() + (r.ReferenceNo is { } reference ? "   (Ref: " + reference + ")" : ""));
            });
            if (r.Remarks is { } remarks) col.Item().Text("Remarks: " + remarks);
            col.Item().Text(t =>
            {
                t.Span("Balance due after this payment: ").SemiBold();
                t.Span(Rs(r.BalanceAfter));
            });
        });

        page.Footer().Row(row =>
        {
            row.RelativeItem().Text("Received by: " + (r.ReceivedByName ?? "-")).FontSize(9);
            row.RelativeItem().AlignRight().Text("Computer-generated receipt. No signature required.").FontSize(8).FontColor(Colors.Grey.Darken1);
        });
    })).GeneratePdf();

    /// <summary>"Rs. 12,500" – the ₹ glyph is missing from many PDF fonts.</summary>
    private static string Rs(decimal amount) =>
        "Rs. " + amount.ToString(amount == decimal.Truncate(amount) ? "N0" : "N2", India);

    private static IContainer HeaderCell(IContainer c) =>
        c.BorderBottom(1).PaddingVertical(4).DefaultTextStyle(x => x.SemiBold());

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3);
}
