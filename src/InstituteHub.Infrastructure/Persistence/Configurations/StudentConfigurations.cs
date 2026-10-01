using InstituteHub.Domain.Students;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static InstituteHub.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace InstituteHub.Infrastructure.Persistence.Configurations;

public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> e)
    {
        e.HasTenant();
        e.Property(x => x.AdmissionNo).HasMaxLength(30).IsRequired();
        e.Property(x => x.FirstName).HasMaxLength(60).IsRequired();
        e.Property(x => x.LastName).HasMaxLength(60);
        e.Property(x => x.Gender).HasMaxLength(10);
        e.Property(x => x.Phone).HasMaxLength(PhoneLength);
        e.Property(x => x.SchoolName).HasMaxLength(150);
        e.Property(x => x.ClassGrade).HasMaxLength(30);
        e.Property(x => x.Address).HasMaxLength(300);
        e.Property(x => x.Notes).HasColumnType("text");
        e.Ignore(x => x.FullName);

        e.HasIndex(x => new { x.TenantId, x.AdmissionNo }).IsUnique().HasFilter("is_deleted = false");
        e.HasIndex(x => new { x.TenantId, x.Status, x.FirstName });
    }
}

public class GuardianConfiguration : IEntityTypeConfiguration<Guardian>
{
    public void Configure(EntityTypeBuilder<Guardian> e)
    {
        e.HasTenant();
        e.Property(x => x.FullName).HasMaxLength(120).IsRequired();
        e.Property(x => x.Phone).HasMaxLength(PhoneLength).IsRequired();
        e.Property(x => x.AltPhone).HasMaxLength(PhoneLength);
        e.Property(x => x.Email).HasMaxLength(150);
        e.Property(x => x.WhatsAppOptIn).HasColumnName("whatsapp_opt_in");

        e.HasIndex(x => new { x.TenantId, x.Phone });
    }
}

public class StudentGuardianConfiguration : IEntityTypeConfiguration<StudentGuardian>
{
    public void Configure(EntityTypeBuilder<StudentGuardian> e)
    {
        e.HasKey(x => new { x.StudentId, x.GuardianId });
        e.HasTenant();
        e.HasOne(x => x.Student).WithMany(s => s.Guardians).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Guardian).WithMany(g => g.Students).HasForeignKey(x => x.GuardianId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.GuardianId);
    }
}
