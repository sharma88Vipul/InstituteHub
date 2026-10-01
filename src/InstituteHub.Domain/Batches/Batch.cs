using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Batches;

/// <summary>Bit mask stored as smallint: Mon=1, Tue=2, Wed=4 ... Sun=64.</summary>
[Flags]
public enum WeekDays : short
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
}

public class Batch : TenantEntity, IAuditable
{
    public string Name { get; set; } = "";
    public string? Subject { get; set; }
    /// <summary>FK to the Identity user who teaches this batch.</summary>
    public Guid? TeacherId { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public WeekDays DaysOfWeek { get; set; }
    public int? Capacity { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public Guid? DefaultFeePlanId { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Enrollment> Enrollments { get; } = [];
}
