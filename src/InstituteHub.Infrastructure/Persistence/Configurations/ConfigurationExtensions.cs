using InstituteHub.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteHub.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    public const int PhoneLength = 16;   // E.164, e.g. +919876543210

    /// <summary>tenant_id NOT NULL with a FK to tenants (no cascade delete).</summary>
    public static void HasTenant<T>(this EntityTypeBuilder<T> e) where T : class, ITenantOwned
    {
        // String-based so EF maps the implementing property, not the interface member.
        e.Property(nameof(ITenantOwned.TenantId)).IsRequired();
        e.HasOne<Domain.Tenants.Tenant>().WithMany()
            .HasForeignKey(nameof(ITenantOwned.TenantId))
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>PostgreSQL xmin system column as optimistic concurrency token.</summary>
    public static void HasXminConcurrencyToken<T>(this EntityTypeBuilder<T> e) where T : class =>
        e.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
}
