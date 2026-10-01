using InstituteHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static InstituteHub.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace InstituteHub.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> e)
    {
        e.Property(x => x.Name).HasMaxLength(150).IsRequired();
        e.Property(x => x.Slug).HasMaxLength(60).IsRequired();
        e.Property(x => x.OwnerName).HasMaxLength(120).IsRequired();
        e.Property(x => x.Phone).HasMaxLength(PhoneLength).IsRequired();
        e.Property(x => x.Email).HasMaxLength(150);
        e.Property(x => x.AddressLine).HasMaxLength(200);
        e.Property(x => x.City).HasMaxLength(80);
        e.Property(x => x.State).HasMaxLength(80);
        e.Property(x => x.Pincode).HasMaxLength(10);
        e.Property(x => x.Gstin).HasMaxLength(15);
        e.Property(x => x.LogoPath).HasMaxLength(300);
        e.Property(x => x.TimeZone).HasMaxLength(40).IsRequired().HasDefaultValue("Asia/Kolkata");
        e.Property(x => x.ReceiptPrefix).HasMaxLength(10).IsRequired();

        e.HasIndex(x => x.Slug).IsUnique();
    }
}
