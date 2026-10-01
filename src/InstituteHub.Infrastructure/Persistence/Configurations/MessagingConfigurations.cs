using InstituteHub.Domain.Messaging;
using InstituteHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static InstituteHub.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace InstituteHub.Infrastructure.Persistence.Configurations;

public class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> e)
    {
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.Code).HasMaxLength(40).IsRequired();
        e.Property(x => x.Channel).HasMaxLength(10);
        e.Property(x => x.ProviderTemplateId).HasMaxLength(100);
        e.Property(x => x.Body).HasColumnType("text").IsRequired();

        e.HasIndex(x => new { x.TenantId, x.Code, x.Channel });
    }
}

public class MessageLogConfiguration : IEntityTypeConfiguration<MessageLog>
{
    public void Configure(EntityTypeBuilder<MessageLog> e)
    {
        e.HasTenant();
        e.Property(x => x.TemplateCode).HasMaxLength(40).IsRequired();
        e.Property(x => x.Channel).HasMaxLength(10);
        e.Property(x => x.ToPhone).HasMaxLength(PhoneLength).IsRequired();
        e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        e.Property(x => x.RelatedEntity).HasMaxLength(30);
        e.Property(x => x.ProviderMessageId).HasMaxLength(100);
        e.Property(x => x.Error).HasMaxLength(500);

        e.HasIndex(x => new { x.TenantId, x.CreatedAt }).IsDescending(false, true);
        e.HasIndex(x => x.ProviderMessageId);
    }
}
