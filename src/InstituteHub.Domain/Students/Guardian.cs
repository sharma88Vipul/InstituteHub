using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Students;

public enum GuardianRelation { Father, Mother, Guardian, Self }

public class Guardian : TenantEntity, IAuditable
{
    public string FullName { get; set; } = "";
    public GuardianRelation Relation { get; set; } = GuardianRelation.Father;
    /// <summary>Main WhatsApp number in E.164 format, e.g. +919876543210.</summary>
    public string Phone { get; set; } = "";
    public string? AltPhone { get; set; }
    public string? Email { get; set; }
    public bool WhatsAppOptIn { get; set; }
    public DateTimeOffset? OptInAt { get; set; }

    public ICollection<StudentGuardian> Students { get; } = [];
}
