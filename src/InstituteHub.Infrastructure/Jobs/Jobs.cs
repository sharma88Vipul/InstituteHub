using Hangfire;
using InstituteHub.Application.Fees;
using InstituteHub.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InstituteHub.Infrastructure.Jobs;

// Background jobs from design doc 5.5. Every job is safe to run twice: dues are keyed by period_key, reminders are
// not repeated on the same day, and receipts/absent alerts check message_logs before sending.

/// <summary>Daily 00:30 IST: creates the current month's dues for monthly plans (and any missing dues).</summary>
public sealed class MonthlyDueJob(TenantJobRunner runner)
{
    [AutomaticRetry(Attempts = 3)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public Task RunAsync(CancellationToken ct) =>
        runner.ForEachActiveTenantAsync(nameof(MonthlyDueJob), async (services, token) =>
        {
            var result = await services.GetRequiredService<FeeDueService>().GenerateMissingDuesAsync(ct: token);
            return result.IsSuccess
                ? $"{result.Value.DuesCreated} due(s) created for {result.Value.EnrollmentsChecked} enrollment(s)"
                : string.Join("; ", result.Errors.Select(e => e.Message));
        }, ct);
}

/// <summary>Daily 10:00 IST: fee reminders to guardians (design doc 6.6). Also run on demand for one institute.</summary>
public sealed class FeeReminderJob(TenantJobRunner runner, ILogger<FeeReminderJob> logger)
{
    [AutomaticRetry(Attempts = 3)]
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunAsync(CancellationToken ct) =>
        runner.ForEachActiveTenantAsync(nameof(FeeReminderJob), async (services, token) =>
            (await services.GetRequiredService<MessagingService>().SendFeeRemindersAsync(token)).ToString(), ct);

    [AutomaticRetry(Attempts = 3)]
    public async Task RunForTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var result = await runner.RunAsync(tenantId, services =>
            services.GetRequiredService<MessagingService>().SendFeeRemindersAsync(ct));
        logger.LogInformation("Fee reminders (on demand) for institute {TenantId}: {Summary}", tenantId, result);
    }
}

/// <summary>On demand after a payment: WhatsApp/SMS receipt with a public link to the primary guardian.</summary>
public sealed class SendReceiptJob(TenantJobRunner runner, ILogger<SendReceiptJob> logger)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(Guid tenantId, Guid paymentId, bool resend, CancellationToken ct)
    {
        var result = await runner.RunAsync(tenantId, services =>
            services.GetRequiredService<MessagingService>().SendPaymentReceiptAsync(paymentId, resend, ct));
        logger.LogInformation("Receipt message for payment {PaymentId}: {Summary}", paymentId, result);
    }
}

/// <summary>On demand after attendance is saved: alerts guardians of students marked absent today.</summary>
public sealed class AbsentAlertJob(TenantJobRunner runner, ILogger<AbsentAlertJob> logger)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(Guid tenantId, Guid attendanceSessionId, CancellationToken ct)
    {
        var result = await runner.RunAsync(tenantId, services =>
            services.GetRequiredService<MessagingService>().SendAbsentAlertsAsync(attendanceSessionId, ct));
        logger.LogInformation("Absent alerts for register {SessionId}: {Summary}", attendanceSessionId, result);
    }
}
