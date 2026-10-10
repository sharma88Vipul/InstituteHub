using Hangfire;
using InstituteHub.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace InstituteHub.Infrastructure.Jobs;

/// <summary>Queues jobs in Hangfire for the current institute (the tenant id travels as a job argument).</summary>
public sealed class HangfireBackgroundJobs(
    IBackgroundJobClient client,
    ITenantProvider tenant,
    ILogger<HangfireBackgroundJobs> logger) : IBackgroundJobs
{
    public void EnqueueReceipt(Guid paymentId, bool resend = false) =>
        Enqueue("receipt message", tenantId =>
            client.Enqueue<SendReceiptJob>(j => j.RunAsync(tenantId, paymentId, resend, CancellationToken.None)));

    public void EnqueueAbsentAlerts(Guid attendanceSessionId) =>
        Enqueue("absent alerts", tenantId =>
            client.Enqueue<AbsentAlertJob>(j => j.RunAsync(tenantId, attendanceSessionId, CancellationToken.None)));

    public void EnqueueFeeReminders() =>
        Enqueue("fee reminders", tenantId =>
            client.Enqueue<FeeReminderJob>(j => j.RunForTenantAsync(tenantId, CancellationToken.None)));

    private void Enqueue(string what, Func<Guid, string> enqueue)
    {
        if (tenant.CurrentTenantId is not { } tenantId)
        {
            logger.LogWarning("Cannot queue {What} without an institute context", what);
            return;
        }

        try
        {
            var jobId = enqueue(tenantId);
            logger.LogDebug("Queued {What} as job {JobId}", what, jobId);
        }
        catch (Exception ex)
        {
            // The payment/attendance is already saved; a missing message must not turn that into an error page.
            logger.LogError(ex, "Could not queue {What}", what);
        }
    }
}
