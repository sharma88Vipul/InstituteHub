using InstituteHub.Domain.Attendance;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Students;
using InstituteHub.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InstituteHub.Infrastructure.Persistence.Configurations;

public class AttendanceSessionConfiguration : IEntityTypeConfiguration<AttendanceSession>
{
    public void Configure(EntityTypeBuilder<AttendanceSession> e)
    {
        e.HasTenant();
        e.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.MarkedBy).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Records).WithOne().HasForeignKey(r => r.SessionId).OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(x => new { x.BatchId, x.SessionDate }).IsUnique();
    }
}

public class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> e)
    {
        e.HasTenant();
        e.Property(x => x.Status).HasMaxLength(10);
        e.Property(x => x.Remark).HasMaxLength(200);
        e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);

        e.HasIndex(x => new { x.SessionId, x.StudentId }).IsUnique();
        e.HasIndex(x => x.StudentId);
    }
}
