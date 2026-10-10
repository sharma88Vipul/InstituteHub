using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Attendance;
using InstituteHub.Application.Batches;
using InstituteHub.Application.Common;
using InstituteHub.Application.Fees;
using InstituteHub.Application.Messaging;
using InstituteHub.Application.Payments;
using InstituteHub.Application.Students;
using InstituteHub.Application.Tenants;
using InstituteHub.Domain.Attendance;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Messaging;
using InstituteHub.Domain.Students;
using InstituteHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.IntegrationTests;

/// <summary>Week 7 "done when": reminders go out and statuses update (FakeMessageSender).</summary>
[Collection(PostgresCollection.Name)]
public class MessagingTests(PostgresWebAppFactory factory)
{
    private sealed record Setup(Guid TenantId, Guid OwnerId, Guid StudentId, Guid BatchId, DateOnly Today);

    /// <summary>Institute (on trial) with one student whose one-time fee of ₹5,000 is due today.</summary>
    private async Task<Setup> ArrangeAsync(string institute, bool whatsAppOptIn = true)
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
        var plan = await owner.Get<FeePlanService>().CreateAsync(new FeePlanRequest("Course fee", BillingType.OneTime, 5000, null, null, null, 5));
        plan.IsSuccess.ShouldBeTrue(string.Join("; ", plan.Errors.Select(e => e.Message)));
        var batch = await owner.Get<BatchService>().CreateAsync(new BatchRequest(
            "Morning", null, null, null, null, (WeekDays)127, null, today.AddMonths(-1), null, plan.Value));
        var student = await owner.Get<StudentService>().AdmitAsync(new AdmissionRequest(
            null, "Aarav", "Sharma", null, null, null, null, null, null, today, null, null,
            new GuardianInput("Rakesh Sharma", GuardianRelation.Father, "9815600002", null, null, whatsAppOptIn),
            batch.Value, null, 0));
        student.IsSuccess.ShouldBeTrue(string.Join("; ", student.Errors.Select(e => e.Message)));

        return new Setup(signUp.TenantId, signUp.UserId, student.Value, batch.Value, today);
    }

    private TestScope AsOwner(Guid tenantId, Guid userId) => new(factory.Services, tenantId, userId, Roles.Owner);
    private TestScope AsOwner(Setup s) => AsOwner(s.TenantId, s.OwnerId);

    /// <summary>Like a Hangfire job: institute set, nobody signed in.</summary>
    private TestScope AsJob(Setup s) => new(factory.Services, s.TenantId);

    [Fact]
    public async Task Fee_reminder_goes_out_once_a_day_and_is_logged()
    {
        var s = await ArrangeAsync("Reminder Classes");

        await using (var job = AsJob(s))
        {
            var first = await job.Get<MessagingService>().SendFeeRemindersAsync();
            first.Sent.ShouldBe(1);
        }

        await using (var job = AsJob(s))
        {
            var again = await job.Get<MessagingService>().SendFeeRemindersAsync();
            again.Sent.ShouldBe(0); // same day: no repeat, even if the job is retried

            var log = await job.Db.MessageLogs.SingleAsync();
            log.TemplateCode.ShouldBe(MessageTemplateCodes.FeeReminder);
            log.Channel.ShouldBe(MessageChannel.WhatsApp);
            log.Status.ShouldBe(MessageStatus.Sent);
            log.StudentId.ShouldBe(s.StudentId);
            log.ToPhone.ShouldBe("+919815600002");
            log.ProviderMessageId.ShouldNotBeNull().ShouldStartWith("fake-");
            log.Payload.ShouldContain("5,000");

            var due = await job.Db.FeeDues.SingleAsync(d => d.StudentId == s.StudentId);
            due.ReminderCount.ShouldBe((short)1);
            due.LastReminderAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Delivery_updates_only_move_forward()
    {
        var s = await ArrangeAsync("Webhook Classes");
        string providerId;
        await using (var job = AsJob(s))
        {
            await job.Get<MessagingService>().SendFeeRemindersAsync();
            providerId = (await job.Db.MessageLogs.SingleAsync()).ProviderMessageId!;
        }

        // The webhook request has neither a tenant nor a user.
        await using (var webhook = new TestScope(factory.Services, tenantId: null))
        {
            var messaging = webhook.Get<MessagingService>();
            (await messaging.ApplyDeliveryStatusAsync(providerId, MessageStatus.Read, null)).ShouldBeTrue();
            (await messaging.ApplyDeliveryStatusAsync(providerId, MessageStatus.Delivered, null)).ShouldBeTrue(); // late, ignored
            (await messaging.ApplyDeliveryStatusAsync("fake-unknown", MessageStatus.Delivered, null)).ShouldBeFalse();
        }

        await using var check = AsJob(s);
        (await check.Db.MessageLogs.SingleAsync()).Status.ShouldBe(MessageStatus.Read);
    }

    [Fact]
    public async Task Payment_receipt_is_sent_once_with_a_working_public_link()
    {
        var s = await ArrangeAsync("Receipt Message Classes");
        Guid paymentId;
        await using (var owner = AsOwner(s))
        {
            var payment = await owner.Get<PaymentService>().RecordAsync(
                new RecordPaymentRequest(s.StudentId, s.Today, 5000, PaymentMode.UPI, "UPI123", null, null));
            payment.IsSuccess.ShouldBeTrue(string.Join("; ", payment.Errors.Select(e => e.Message)));
            paymentId = payment.Value;
        }

        await using (var job = AsJob(s))
        {
            var messaging = job.Get<MessagingService>();
            (await messaging.SendPaymentReceiptAsync(paymentId)).Sent.ShouldBe(1);
            (await messaging.SendPaymentReceiptAsync(paymentId)).Sent.ShouldBe(0); // retried job: no second message
        }

        await using var check = AsJob(s);
        var log = await check.Db.MessageLogs.SingleAsync(m => m.TemplateCode == MessageTemplateCodes.PaymentReceipt);
        log.RelatedEntity.ShouldBe(MessageRelated.Payment);
        log.RelatedId.ShouldBe(paymentId);

        var link = System.Text.Json.JsonDocument.Parse(log.Payload).RootElement.GetProperty("receipt_link").GetString()!;
        link.ShouldStartWith("https://test.local/r/");
        var token = link["https://test.local/r/".Length..];
        var read = check.Get<IReceiptLinks>().Read(token);
        read.HasValue.ShouldBeTrue();
        read.GetValueOrDefault().TenantId.ShouldBe(s.TenantId);
        read.GetValueOrDefault().PaymentId.ShouldBe(paymentId);
        check.Get<IReceiptLinks>().Read(token[..^2] + "xx").ShouldBeNull();
    }

    [Fact]
    public async Task Guardian_without_whatsapp_consent_gets_sms_on_trial()
    {
        var s = await ArrangeAsync("Sms Classes", whatsAppOptIn: false);

        await using var job = AsJob(s);
        (await job.Get<MessagingService>().SendFeeRemindersAsync()).Sent.ShouldBe(1);
        (await job.Db.MessageLogs.SingleAsync()).Channel.ShouldBe(MessageChannel.Sms);
    }

    [Fact]
    public async Task Absent_alert_goes_out_once_per_student_per_register()
    {
        var s = await ArrangeAsync("Absent Classes");
        await using (var owner = AsOwner(s))
        {
            var saved = await owner.Get<AttendanceService>().SaveAsync(new SaveAttendanceRequest(
                s.BatchId, s.Today, [new AttendanceMark(s.StudentId, AttendanceStatus.Absent)]));
            saved.IsSuccess.ShouldBeTrue(string.Join("; ", saved.Errors.Select(e => e.Message)));
        }

        await using var job = AsJob(s);
        var sessionId = await job.Db.AttendanceSessions.Where(x => x.BatchId == s.BatchId).Select(x => x.Id).SingleAsync();
        var messaging = job.Get<MessagingService>();
        (await messaging.SendAbsentAlertsAsync(sessionId)).Sent.ShouldBe(1);
        (await messaging.SendAbsentAlertsAsync(sessionId)).Sent.ShouldBe(0);

        var log = await job.Db.MessageLogs.SingleAsync(m => m.TemplateCode == MessageTemplateCodes.AbsentAlert);
        log.Payload.ShouldContain("Morning");
    }

    [Fact]
    public async Task Reminders_run_for_one_institute_never_touch_another()
    {
        var a = await ArrangeAsync("Isolation A Classes");
        var b = await ArrangeAsync("Isolation B Classes");

        await using (var job = AsJob(a))
        {
            (await job.Get<MessagingService>().SendFeeRemindersAsync()).Sent.ShouldBe(1);
        }

        await using var checkB = AsJob(b);
        (await checkB.Db.MessageLogs.CountAsync()).ShouldBe(0);
        (await checkB.Db.FeeDues.SingleAsync()).ReminderCount.ShouldBe((short)0);
    }
}
