namespace InstituteHub.Application.Abstractions;

/// <summary>Wraps TimeProvider so tests can fix "today". Business dates are in the institute's time zone.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Today's date in the given IANA time zone (default Asia/Kolkata).</summary>
    DateOnly Today(string timeZoneId = "Asia/Kolkata");
}
