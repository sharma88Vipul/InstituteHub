using InstituteHub.Domain.Tenants;
using InstituteHub.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static InstituteHub.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace InstituteHub.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> e)
    {
        e.Property(x => x.FullName).HasMaxLength(120).IsRequired();
        e.Property(x => x.PhoneNumber).HasMaxLength(PhoneLength);
        e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.TenantId);
    }
}
