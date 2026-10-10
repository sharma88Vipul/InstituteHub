using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Payments;
using InstituteHub.Infrastructure.Identity;
using InstituteHub.Web.Security;

namespace InstituteHub.Web.Endpoints;

public static class ReceiptEndpoints
{
    public static IEndpointRouteBuilder MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /receipts/{paymentId}.pdf – the receipt PDF, shown in the browser so it can be printed or saved.
        app.MapGet("/receipts/{paymentId:guid}.pdf", async (Guid paymentId, PaymentService payments, IPdfService pdf, CancellationToken ct) =>
            {
                // Tenant query filters apply: another institute's payment id simply returns 404.
                var receipt = await payments.GetReceiptAsync(paymentId, ct);
                return receipt is null
                    ? Results.NotFound()
                    : Results.File(pdf.RenderReceipt(receipt), "application/pdf");
            })
            .RequireAuthorization(Policies.RecordPayments)
            .WithName("ReceiptPdf");

        // GET /r/{token} – public receipt link sent to parents on WhatsApp/SMS (design doc 5.4). No sign-in: the signed,
        // expiring token names the institute and the payment, and that institute becomes the tenant for this request.
        app.MapGet("/r/{token}", async (string token, IReceiptLinks links, JobTenantContext tenantContext,
                PaymentService payments, IPdfService pdf, CancellationToken ct) =>
            {
                if (links.Read(token) is not { } link) return Results.NotFound(); // altered or expired: same answer

                tenantContext.TenantId = link.TenantId;
                var receipt = await payments.GetReceiptForPublicLinkAsync(link.PaymentId, ct);
                return receipt is null
                    ? Results.NotFound()
                    : Results.File(pdf.RenderReceipt(receipt), "application/pdf");
            })
            .AllowAnonymous()
            .WithName("PublicReceipt");

        return app;
    }
}
