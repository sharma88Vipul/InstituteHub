using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Attendance;

public enum AttendanceStatus { Present, Absent, Late, Leave }

public class AttendanceRecord : TenantEntity
{
    public Guid SessionId { get; set; }
    public Guid StudentId { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;
    public string? Remark { get; set; }
}
