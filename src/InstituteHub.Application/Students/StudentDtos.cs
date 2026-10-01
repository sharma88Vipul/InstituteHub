using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Students;

namespace InstituteHub.Application.Students;

public sealed record StudentQuery(
    string? Search = null,
    StudentStatus? Status = StudentStatus.Active,
    Guid? BatchId = null,
    int Page = 1,
    int PageSize = 25);

public sealed record StudentListItem(
    Guid Id,
    string AdmissionNo,
    string FullName,
    string? ClassGrade,
    StudentStatus Status,
    string? GuardianName,
    string? GuardianPhone,
    IReadOnlyList<string> Batches);

public sealed record GuardianInput(
    string FullName,
    GuardianRelation Relation,
    string Phone,
    string? AltPhone,
    string? Email,
    bool WhatsAppOptIn);

/// <summary>Admission form: student + guardian + (optional) batch and fee plan in one go (design doc 6.2).</summary>
public sealed record AdmissionRequest(
    string? AdmissionNo,
    string FirstName,
    string? LastName,
    string? Gender,
    DateOnly? DateOfBirth,
    string? Phone,
    string? SchoolName,
    string? ClassGrade,
    string? Address,
    DateOnly AdmissionDate,
    string? Notes,
    Guid? ExistingGuardianId,
    GuardianInput? NewGuardian,
    Guid? BatchId,
    Guid? FeePlanId,
    decimal DiscountAmount);

public sealed record UpdateStudentRequest(
    string AdmissionNo,
    string FirstName,
    string? LastName,
    string? Gender,
    DateOnly? DateOfBirth,
    string? Phone,
    string? SchoolName,
    string? ClassGrade,
    string? Address,
    DateOnly AdmissionDate,
    string? Notes);

public sealed record GuardianSummary(
    Guid Id,
    string FullName,
    GuardianRelation Relation,
    string Phone,
    string? AltPhone,
    string? Email,
    bool WhatsAppOptIn,
    bool IsPrimary,
    IReadOnlyList<string> OtherChildren);

public sealed record EnrollmentSummary(
    Guid Id,
    Guid BatchId,
    string BatchName,
    string FeePlanName,
    DateOnly EnrolledOn,
    DateOnly? EndedOn,
    decimal DiscountAmount,
    EnrollmentStatus Status);

public sealed record StudentDetails(
    Guid Id,
    string AdmissionNo,
    string FirstName,
    string? LastName,
    string? Gender,
    DateOnly? DateOfBirth,
    string? Phone,
    string? SchoolName,
    string? ClassGrade,
    string? Address,
    DateOnly AdmissionDate,
    StudentStatus Status,
    string? Notes,
    IReadOnlyList<GuardianSummary> Guardians,
    IReadOnlyList<EnrollmentSummary> Enrollments)
{
    public string FullName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}

/// <summary>An existing guardian found by phone number – offered for linking siblings.</summary>
public sealed record GuardianMatch(Guid Id, string FullName, string Phone, IReadOnlyList<string> Children);
