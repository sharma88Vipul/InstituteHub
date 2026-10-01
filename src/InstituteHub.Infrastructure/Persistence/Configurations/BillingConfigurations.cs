using InstituteHub.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteHub.Infrastructure.Persistence.Configurations;

public class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> e)
    {
        e.Property(x => x.Code).HasMaxLength(20).IsRequired();
        e.Property(x => x.Name).HasMaxLength(60).IsRequired();
        e.Property(x => x.PriceMonthly).HasPrecision(10, 2);
        e.Property(x => x.PriceYearly).HasPrecision(10, 2);
        e.Property(x => x.Features).HasColumnType("jsonb").IsRequired();
        e.HasIndex(x => x.Code).IsUnique();
    }
}

public class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> e)
    {
        e.HasTenant();
        e.Property(x => x.BillingCycle).HasMaxLength(10);
        e.Property(x => x.ExternalSubscriptionId).HasMaxLength(100);
        e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.TenantId);
    }
}
