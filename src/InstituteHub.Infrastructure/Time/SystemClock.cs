using InstituteHub.Application.Abstractions;

namespace InstituteHub.Infrastructure.Time;

public sealed class SystemClock(TimeProvider time) : IClock
{
    public DateTimeOffset UtcNow => time.GetUtcNow();

    public DateOnly Today(string timeZoneId = "Asia/Kolkata")
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, zone).DateTime);
    }
}
