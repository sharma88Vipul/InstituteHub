using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Students;
using InstituteHub.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteHub.Infrastructure.Persistence.Configurations;

public class FeePlanConfiguration : IEntityTypeConfiguration<FeePlan>
{
    public void Configure(EntityTypeBuilder<FeePlan> e)
    {
        e.HasTenant();
        e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        e.ToTable(t => t.HasCheckConstraint("ck_fee_plans_due_day", "due_day_of_month BETWEEN 1 AND 28"));
    }
}

public class FeeDueConfiguration : IEntityTypeConfiguration<FeeDue>
{
    public void Configure(EntityTypeBuilder<FeeDue> e)
    {
        e.HasTenant();
        e.Property(x => x.Title).HasMaxLength(100).IsRequired();
        e.Property(x => x.PeriodKey).HasMaxLength(20);
        e.Ignore(x => x.Balance);

        e.HasOne<Enrollment>().WithMany(en => en.FeeDues).HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);

        e.HasXminConcurrencyToken();

        e.HasIndex(x => new { x.TenantId, x.Status, x.DueDate });
        e.HasIndex(x => x.StudentId);
        e.HasIndex(x => new { x.EnrollmentId, x.PeriodKey }).IsUnique().HasFilter("period_key IS NOT NULL");

        e.ToTable(t =>
        {
            t.HasCheckConstraint("ck_fee_dues_amount", "amount >= 0");
            t.HasCheckConstraint("ck_fee_dues_paid", "paid_amount <= amount - discount_amount");
        });
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> e)
    {
        e.HasTenant();
        e.Property(x => x.ReceiptNumber).HasMaxLength(30).IsRequired();
        e.Property(x => x.ReferenceNo).HasMaxLength(60);
        e.Property(x => x.Remarks).HasMaxLength(300);
        e.Property(x => x.CancelledReason).HasMaxLength(300);

        e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ReceivedBy).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Allocations).WithOne().HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Restrict);

        e.HasXminConcurrencyToken();

        e.HasIndex(x => new { x.TenantId, x.ReceiptNumber }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.PaymentDate });
        e.HasIndex(x => x.StudentId);

        e.ToTable(t => t.HasCheckConstraint("ck_payments_amount", "amount > 0"));
    }
}

public class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> e)
    {
        e.HasTenant();
        e.HasOne(x => x.FeeDue).WithMany().HasForeignKey(x => x.FeeDueId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.FeeDueId);
        e.ToTable(t => t.HasCheckConstraint("ck_payment_allocations_amount", "amount > 0"));
    }
}

public class ReceiptCounterConfiguration : IEntityTypeConfiguration<ReceiptCounter>
{
    public void Configure(EntityTypeBuilder<ReceiptCounter> e)
    {
        e.HasKey(x => new { x.TenantId, x.FinancialYear });
        e.HasTenant();
        e.Property(x => x.FinancialYear).HasMaxLength(7);
    }
}
