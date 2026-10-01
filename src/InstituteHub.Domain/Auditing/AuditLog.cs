namespace InstituteHub.Domain.Auditing;

public enum AuditAction { Create, Update, Delete, Cancel }

/// <summary>Written only by the SaveChanges interceptor. Append-only; never updated.</summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string EntityName { get; set; } = "";
    public Guid EntityId { get; set; }
    public AuditAction Action { get; set; }
    /// <summary>Old and new values of changed fields, as jsonb.</summary>
    public string? Changes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
