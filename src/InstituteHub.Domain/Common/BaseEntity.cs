namespace InstituteHub.Domain.Common;

/// <summary>
/// Base for every persisted entity: UUID v7 key, audit columns and soft delete (design doc 4.1).
/// Audit columns are stamped by the SaveChanges interceptor, never by hand.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
}
