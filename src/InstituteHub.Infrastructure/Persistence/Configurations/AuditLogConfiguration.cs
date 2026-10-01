using InstituteHub.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteHub.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> e)
    {
        e.Property(x => x.EntityName).HasMaxLength(60).IsRequired();
        e.Property(x => x.Action).HasMaxLength(10);
        e.Property(x => x.Changes).HasColumnType("jsonb");
        e.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId });
    }
}
