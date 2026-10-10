using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace InstituteHub.Infrastructure.Jobs;

/// <summary>Schedules from design doc 5.5, in Indian time. Called once at start-up (AddOrUpdate is idempotent).</summary>
public static class RecurringJobs
{
    public const string MonthlyDues = "monthly-dues";
    public const string FeeReminders = "fee-reminders";

    public static void Register(IServiceProvider services)
    {
        var manager = services.GetRequiredService<IRecurringJobManager>();
        var options = new RecurringJobOptions { TimeZone = India() };

        manager.AddOrUpdate<MonthlyDueJob>(MonthlyDues, j => j.RunAsync(CancellationToken.None), "30 0 * * *", options);
        manager.AddOrUpdate<FeeReminderJob>(FeeReminders, j => j.RunAsync(CancellationToken.None), "0 10 * * *", options);
    }

    private static TimeZoneInfo India()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); // Windows id
        }
    }
}
