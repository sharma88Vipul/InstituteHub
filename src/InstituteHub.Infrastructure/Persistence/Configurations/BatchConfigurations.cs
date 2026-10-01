using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Fees;
using InstituteHub.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteHub.Infrastructure.Persistence.Configurations;

public class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> e)
    {
        e.HasTenant();
        e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        e.Property(x => x.Subject).HasMaxLength(80);
        e.Property(x => x.DaysOfWeek).HasConversion<short>().HasColumnType("smallint");

        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne<FeePlan>().WithMany().HasForeignKey(x => x.DefaultFeePlanId).OnDelete(DeleteBehavior.SetNull);

        e.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}

public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> e)
    {
        e.HasTenant();
        e.HasOne(x => x.Student).WithMany(s => s.Enrollments).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Batch).WithMany(b => b.Enrollments).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.FeePlan).WithMany().HasForeignKey(x => x.FeePlanId).OnDelete(DeleteBehavior.Restrict);

        // No double enrollment in the same batch.
        e.HasIndex(x => new { x.StudentId, x.BatchId }).IsUnique().HasFilter("status = 'Active'");
        e.HasIndex(x => x.BatchId);
    }
}
