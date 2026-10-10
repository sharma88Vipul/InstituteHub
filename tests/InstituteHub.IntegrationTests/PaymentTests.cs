using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Batches;
using InstituteHub.Application.Common;
using InstituteHub.Application.Fees;
using InstituteHub.Application.Payments;
using InstituteHub.Application.Students;
using InstituteHub.Application.Tenants;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Students;
using InstituteHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.IntegrationTests;

/// <summary>Week 6 "done when": collect a fee and print a receipt.</summary>
[Collection(PostgresCollection.Name)]
public class PaymentTests(PostgresWebAppFactory factory)
{
    private sealed record Setup(Guid TenantId, Guid OwnerId, Guid StudentId, DateOnly Today, string Prefix);

    /// <summary>Institute with one student on a ₹30,000 plan in 3 instalments of ₹10,000.</summary>
    private async Task<Setup> ArrangeAsync(string institute)
    {
        SignUpResult signUp;
        await using (var anonymous = new TestScope(factory.Services, tenantId: null))
        {
            signUp = (await anonymous.Get<ISignUpService>().SignUpAsync(
                new SignUpRequest(institute, "Owner", "9876543210", $"o-{Guid.NewGuid():N}@example.com", "Secret#1234"),
                CancellationToken.None)).Value;
        }

        await using var owner = AsOwner(signUp.TenantId, signUp.UserId);
        var today = owner.Get<IClock>().Today();
        var plan = await owner.Get<FeePlanService>().CreateAsync(new FeePlanRequest("3 inst", BillingType.Instalments, 30000, null, 3, 1, 5));
        var batch = await owner.Get<BatchService>().CreateAsync(new BatchRequest(
            "Batch", null, null, null, null, (WeekDays)127, null, today.AddMonths(-3), null, plan.Value));
        var student = await owner.Get<StudentService>().AdmitAsync(new AdmissionRequest(
            null, "Payer", null, null, null, null, null, null, null, today.AddMonths(-3), null, null,
            new GuardianInput("Parent", GuardianRelation.Father, "9815600001", null, null, true), batch.Value, null, 0));
        student.IsSuccess.ShouldBeTrue(string.Join("; ", student.Errors.Select(e => e.Message)));

        var prefix = await owner.Db.Tenants.Where(t => t.Id == signUp.TenantId).Select(t => t.ReceiptPrefix).SingleAsync();
        return new Setup(signUp.TenantId, signUp.UserId, student.Value, today, prefix);
    }

    private TestScope AsOwner(Guid tenantId, Guid userId) => new(factory.Services, tenantId, userId, Roles.Owner);
    private TestScope AsOwner(Setup s) => AsOwner(s.TenantId, s.OwnerId);

    private static RecordPaymentRequest Pay(Setup s, decimal amount, IReadOnlyList<Allocation>? manual = null) =>
        new(s.StudentId, s.Today, amount, PaymentMode.Cash, null, null, manual);

    [Fact]
    public async Task Payment_is_applied_oldest_first_with_sequential_receipt_numbers()
    {
        var s = await ArrangeAsync("Receipt Classes");
        await using var owner = AsOwner(s);
        var payments = owner.Get<PaymentService>();

        var first = await payments.RecordAsync(Pay(s, 15000));
        first.IsSuccess.ShouldBeTrue(string.Join("; ", first.Errors.Select(e => e.Message)));
        var second = await payments.RecordAsync(Pay(s, 5000));
        second.IsSuccess.ShouldBeTrue();

        var dues = await owner.Db.FeeDues.Where(d => d.StudentId == s.StudentId).OrderBy(d => d.DueDate).ToListAsync();
        dues.Select(d => d.PaidAmount).ShouldBe(new[] { 10000m, 10000m, 0m });
        dues.Select(d => d.Status).ShouldBe(new[] { FeeDueStatus.Paid, FeeDueStatus.Paid, FeeDueStatus.Pending });

        var fy = ReceiptCounter.FinancialYearFor(s.Today);
        var r1 = (await payments.GetReceiptAsync(first.Value))!;
        var r2 = (await payments.GetReceiptAsync(second.Value))!;
        r1.ReceiptNumber.ShouldBe($"{s.Prefix}/{fy}/00001");
        r2.ReceiptNumber.ShouldBe($"{s.Prefix}/{fy}/00002");
        r1.Lines.Select(l => l.Amount).ShouldBe(new[] { 10000m, 5000m });
        r1.AmountInWords.ShouldBe("Rupees Fifteen Thousand Only");
        r2.BalanceAfter.ShouldBe(10000);
    }

    [Fact]
    public async Task Overpayment_is_kept_as_advance_and_manual_split_is_respected()
    {
        var s = await ArrangeAsync("Advance Classes");
        await using var owner = AsOwner(s);
        var payments = owner.Get<PaymentService>();
        var view = (await payments.GetCollectViewAsync(s.StudentId))!;
        view.Dues.Count.ShouldBe(3);

        // Pay the LAST instalment by hand.
        var manual = await payments.RecordAsync(Pay(s, 10000, [new Allocation(view.Dues[2].Id, 10000)]));
        manual.IsSuccess.ShouldBeTrue(string.Join("; ", manual.Errors.Select(e => e.Message)));
        (await owner.Db.FeeDues.SingleAsync(d => d.Id == view.Dues[2].Id)).Status.ShouldBe(FeeDueStatus.Paid);

        // Pay more than everything that is left.
        var over = await payments.RecordAsync(Pay(s, 25000));
        over.IsSuccess.ShouldBeTrue();
        (await payments.GetReceiptAsync(over.Value))!.Unallocated.ShouldBe(5000);
        (await payments.GetCollectViewAsync(s.StudentId))!.AdvanceCredit.ShouldBe(5000);
    }

    [Fact]
    public async Task Cancelling_reverses_the_dues_and_keeps_the_receipt()
    {
        var s = await ArrangeAsync("Cancel Classes");
        Guid paymentId;
        await using (var owner = AsOwner(s))
        {
            paymentId = (await owner.Get<PaymentService>().RecordAsync(Pay(s, 12000))).Value;
        }

        await using (var staff = new TestScope(factory.Services, s.TenantId, null, Roles.Staff))
        {
            (await staff.Get<PaymentService>().CancelAsync(paymentId, "mistake")).FirstError!.Type.ShouldBe(ErrorType.Forbidden);
        }

        await using var asOwner = AsOwner(s);
        var payments = asOwner.Get<PaymentService>();
        (await payments.CancelAsync(paymentId, "Wrong student")).IsSuccess.ShouldBeTrue();

        (await asOwner.Db.FeeDues.Where(d => d.StudentId == s.StudentId).SumAsync(d => d.PaidAmount)).ShouldBe(0);
        var receipt = (await payments.GetReceiptAsync(paymentId))!;
        receipt.IsCancelled.ShouldBeTrue();
        receipt.CancelledReason.ShouldBe("Wrong student");
        (await payments.ListAsync(new PaymentQuery(StudentId: s.StudentId))).TotalCollected.ShouldBe(0);
        (await asOwner.Db.AuditLogs.AnyAsync(a => a.EntityId == paymentId && a.Action == Domain.Auditing.AuditAction.Cancel)).ShouldBeTrue();
    }

    [Fact]
    public async Task Invalid_payments_are_rejected_and_teachers_cannot_collect()
    {
        var s = await ArrangeAsync("Rules Pay Classes");

        await using (var owner = AsOwner(s))
        {
            var payments = owner.Get<PaymentService>();
            (await payments.RecordAsync(Pay(s, 0))).IsFailure.ShouldBeTrue();
            (await payments.RecordAsync(Pay(s, 100) with { PaymentDate = s.Today.AddDays(1) })).IsFailure.ShouldBeTrue();
            (await payments.RecordAsync(Pay(s, 100) with { Mode = PaymentMode.Cheque })).IsFailure.ShouldBeTrue();   // cheque needs a number
            (await owner.Db.Payments.CountAsync()).ShouldBe(0);
        }

        await using var teacher = new TestScope(factory.Services, s.TenantId, null, Roles.Teacher);
        (await teacher.Get<PaymentService>().RecordAsync(Pay(s, 100))).FirstError!.Type.ShouldBe(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Receipt_pdf_is_generated_and_pending_dues_list_updates()
    {
        var s = await ArrangeAsync("Pdf Classes");
        await using var owner = AsOwner(s);
        var paymentId = (await owner.Get<PaymentService>().RecordAsync(Pay(s, 10000))).Value;

        var receipt = (await owner.Get<PaymentService>().GetReceiptAsync(paymentId))!;
        var pdf = owner.Get<IPdfService>().RenderReceipt(receipt);
        pdf.Length.ShouldBeGreaterThan(1000);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 4).ShouldBe("%PDF");

        var pending = await owner.Get<FeeDueService>().GetPendingDuesAsync(new PendingDuesQuery(DueFilter.AllPending));
        pending.TotalBalance.ShouldBe(20000);
        pending.StudentCount.ShouldBe(1);
    }
}
