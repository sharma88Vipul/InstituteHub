namespace InstituteHub.Domain.Common;

/// <summary>
/// Base for tenant-owned business records. The DbContext adds a global query filter
/// (tenant_id = current tenant AND NOT is_deleted) to every subclass.
/// </summary>
public abstract class TenantEntity : BaseEntity, ITenantOwned
{
    public Guid TenantId { get; set; }
}
