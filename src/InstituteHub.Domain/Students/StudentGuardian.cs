using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Students;

/// <summary>Join table: siblings can share a guardian. Primary key is (StudentId, GuardianId).</summary>
public class StudentGuardian : ITenantOwned
{
    public Guid StudentId { get; set; }
    public Guid GuardianId { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>The primary guardian receives reminders by default.</summary>
    public bool IsPrimary { get; set; }

    public Student? Student { get; set; }
    public Guardian? Guardian { get; set; }
}
