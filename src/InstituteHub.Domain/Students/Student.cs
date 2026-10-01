using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Students;

public enum StudentStatus { Active, Inactive, Left }

public class Student : TenantEntity, IAuditable
{
    public string AdmissionNo { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string? LastName { get; set; }
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Phone { get; set; }
    public string? SchoolName { get; set; }
    public string? ClassGrade { get; set; }
    public string? Address { get; set; }
    public DateOnly AdmissionDate { get; set; }
    public StudentStatus Status { get; set; } = StudentStatus.Active;
    public string? Notes { get; set; }

    public string FullName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";

    public ICollection<StudentGuardian> Guardians { get; } = [];
    public ICollection<Enrollment> Enrollments { get; } = [];
}
