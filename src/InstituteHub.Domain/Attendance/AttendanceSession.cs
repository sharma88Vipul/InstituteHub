using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Attendance;

/// <summary>One attendance register per batch per date.</summary>
public class AttendanceSession : TenantEntity
{
    public Guid BatchId { get; set; }
    public DateOnly SessionDate { get; set; }
    public Guid MarkedBy { get; set; }
    public DateTimeOffset MarkedAt { get; set; }

    public ICollection<AttendanceRecord> Records { get; } = [];
}
