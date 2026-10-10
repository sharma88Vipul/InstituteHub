namespace InstituteHub.Application.Abstractions;

/// <summary>
/// Queues work to run after the current request (Hangfire in Infrastructure). Every method uses the
/// current institute: the implementation passes the tenant id into the job, because jobs run outside
/// the request and set it on their own JobTenantContext.
/// Queuing never throws: if the queue is unavailable the failure is logged and the caller carries on.
/// </summary>
public interface IBackgroundJobs
{
    /// <summary>WhatsApp/SMS receipt to the student's primary guardian (SendReceiptJob).</summary>
    void EnqueueReceipt(Guid paymentId, bool resend = false);

    /// <summary>Absent alerts to guardians of students marked absent in an attendance register (AbsentAlertJob).</summary>
    void EnqueueAbsentAlerts(Guid attendanceSessionId);

    /// <summary>Runs today's fee reminders for this institute now instead of waiting for 10:00 (FeeReminderJob).</summary>
    void EnqueueFeeReminders();
}
