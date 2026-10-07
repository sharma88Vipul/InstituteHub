using FluentValidation;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Batches;
using InstituteHub.Application.Common;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Students;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Application.Students;

/// <summary>Students and guardians: search, admission, profile, edits and status changes.</summary>
public sealed class StudentService(
    IAppDbContext db,
    ITenantProvider tenant,
    ICurrentUser user,
    IClock clock,
    EnrollmentService enrollments,
    IValidator<AdmissionRequest> admissionValidator,
    IValidator<UpdateStudentRequest> updateValidator,
    IValidator<GuardianInput> guardianValidator)
{
    /// <summary>Teachers only see students with an active enrollment in a batch they teach.</summary>
    private IQueryable<Student> VisibleStudents()
    {
        var students = db.Students.AsNoTracking();
        if (user.IsTeacherOnly())
        {
            var me = user.UserId;
            students = students.Where(s => s.Enrollments.Any(e =>
                e.Status == EnrollmentStatus.Active && e.Batch!.TeacherId == me));
        }
        return students;
    }

    // ------------------------------------------------------------------ queries

    public async Task<PagedList<StudentListItem>> SearchAsync(StudentQuery query, CancellationToken ct = default)
    {
        var students = VisibleStudents();

        if (query.Status is { } status)
            students = students.Where(s => s.Status == status);

        if (query.BatchId is { } batchId)
            students = students.Where(s => s.Enrollments.Any(e => e.BatchId == batchId && e.Status == EnrollmentStatus.Active));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            var digits = new string(term.Where(char.IsAsciiDigit).ToArray());
            var searchPhone = digits.Length >= 4;

            students = students.Where(s =>
                (s.FirstName + " " + (s.LastName ?? "")).ToLower().Contains(term) ||
                s.AdmissionNo.ToLower().Contains(term) ||
                (searchPhone && s.Phone != null && s.Phone.Contains(digits)) ||
                (searchPhone && s.Guardians.Any(g => g.Guardian!.Phone.Contains(digits))) ||
                s.Guardians.Any(g => g.Guardian!.FullName.ToLower().Contains(term)));
        }

        var pageSize = Math.Clamp(query.PageSize, 5, 100);
        var page = Math.Max(1, query.Page);
        var total = await students.CountAsync(ct);

        var items = await students
            .OrderBy(s => s.FirstName).ThenBy(s => s.LastName).ThenBy(s => s.AdmissionNo)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StudentListItem(
                s.Id,
                s.AdmissionNo,
                s.FirstName + (s.LastName == null ? "" : " " + s.LastName),
                s.ClassGrade,
                s.Status,
                s.Guardians.OrderByDescending(g => g.IsPrimary).Select(g => g.Guardian!.FullName).FirstOrDefault(),
                s.Guardians.OrderByDescending(g => g.IsPrimary).Select(g => g.Guardian!.Phone).FirstOrDefault(),
                s.Enrollments.Where(e => e.Status == EnrollmentStatus.Active).Select(e => e.Batch!.Name).ToList()))
            .ToListAsync(ct);

        return new PagedList<StudentListItem>(items, page, pageSize, total);
    }

    public async Task<StudentDetails?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await VisibleStudents()
            .Include(x => x.Guardians).ThenInclude(g => g.Guardian)
            .Include(x => x.Enrollments).ThenInclude(e => e.Batch)
            .Include(x => x.Enrollments).ThenInclude(e => e.FeePlan)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return null;

        var guardianIds = s.Guardians.Select(g => g.GuardianId).ToList();
        var siblings = await db.StudentGuardians.AsNoTracking()
            .Where(sg => guardianIds.Contains(sg.GuardianId) && sg.StudentId != id)
            .Select(sg => new
            {
                sg.GuardianId,
                Name = sg.Student!.FirstName + (sg.Student.LastName == null ? "" : " " + sg.Student.LastName),
            })
            .ToListAsync(ct);

        var guardians = s.Guardians
            .Where(g => g.Guardian is not null)
            .OrderByDescending(g => g.IsPrimary).ThenBy(g => g.Guardian!.FullName)
            .Select(g => new GuardianSummary(
                g.GuardianId, g.Guardian!.FullName, g.Guardian.Relation, g.Guardian.Phone, g.Guardian.AltPhone,
                g.Guardian.Email, g.Guardian.WhatsAppOptIn, g.IsPrimary,
                siblings.Where(x => x.GuardianId == g.GuardianId).Select(x => x.Name).ToList()))
            .ToList();

        var enrollmentList = s.Enrollments
            .OrderByDescending(e => e.Status == EnrollmentStatus.Active).ThenByDescending(e => e.EnrolledOn)
            .Select(e => new EnrollmentSummary(
                e.Id, e.BatchId, e.Batch?.Name ?? "(deleted batch)", e.FeePlan?.Name ?? "-",
                e.EnrolledOn, e.EndedOn, e.DiscountAmount, e.Status))
            .ToList();

        return new StudentDetails(s.Id, s.AdmissionNo, s.FirstName, s.LastName, s.Gender, s.DateOfBirth, s.Phone,
            s.SchoolName, s.ClassGrade, s.Address, s.AdmissionDate, s.Status, s.Notes, guardians, enrollmentList);
    }

    /// <summary>Finds guardians already registered with this phone number, so siblings share one guardian.</summary>
    public async Task<IReadOnlyList<GuardianMatch>> FindGuardiansByPhoneAsync(string? phone, CancellationToken ct = default)
    {
        if (PhoneNumber.Normalize(phone) is not { } normalized) return [];

        return await db.Guardians.AsNoTracking()
            .Where(g => g.Phone == normalized || g.AltPhone == normalized)
            .OrderBy(g => g.FullName)
            .Select(g => new GuardianMatch(
                g.Id, g.FullName, g.Phone,
                g.Students.Select(sg => sg.Student!.FirstName + (sg.Student.LastName == null ? "" : " " + sg.Student.LastName)).ToList()))
            .Take(5)
            .ToListAsync(ct);
    }

    // ------------------------------------------------------------------ commands

    /// <summary>
    /// Admission (design doc 6.2): checks the plan's student limit, creates or links the guardian,
    /// creates the student and, when a batch is chosen, the enrollment – all in one SaveChanges.
    /// </summary>
    public async Task<Result<Guid>> AdmitAsync(AdmissionRequest request, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure<Guid>(Error.Forbidden());

        var validation = await admissionValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Result.Invalid<Guid>(validation);

        // Plan limit: active students < max_students of the current subscription plan.
        var maxStudents = await db.TenantSubscriptions
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .Select(s => (int?)s.Plan!.MaxStudents)
            .FirstOrDefaultAsync(ct);
        if (maxStudents is { } max && await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct) >= max)
        {
            return Result.Failure<Guid>(Error.LimitReached(
                $"Your plan allows up to {max} active students. Upgrade your plan or mark students who left as 'Left'."));
        }

        var admissionNo = string.IsNullOrWhiteSpace(request.AdmissionNo)
            ? await NextAdmissionNoAsync(ct)
            : request.AdmissionNo.Trim();
        if (await db.Students.AnyAsync(s => s.AdmissionNo == admissionNo, ct))
        {
            return Result.Failure<Guid>(Error.Conflict("AdmissionNo.Taken", $"Admission number {admissionNo} is already used."));
        }

        Guardian guardian;
        if (request.ExistingGuardianId is { } guardianId)
        {
            var existing = await db.Guardians.FirstOrDefaultAsync(g => g.Id == guardianId, ct);
            if (existing is null) return Result.Failure<Guid>(Error.NotFound("Guardian"));
            guardian = existing;
        }
        else
        {
            guardian = NewGuardian(request.NewGuardian!);
            db.Guardians.Add(guardian);
        }

        var student = new Student
        {
            AdmissionNo = admissionNo,
            FirstName = request.FirstName.Trim(),
            LastName = Clean(request.LastName),
            Gender = Clean(request.Gender),
            DateOfBirth = request.DateOfBirth,
            Phone = PhoneNumber.Normalize(request.Phone),
            SchoolName = Clean(request.SchoolName),
            ClassGrade = Clean(request.ClassGrade),
            Address = Clean(request.Address),
            AdmissionDate = request.AdmissionDate,
            Notes = Clean(request.Notes),
            Status = StudentStatus.Active,
        };
        db.Students.Add(student);
        db.StudentGuardians.Add(new StudentGuardian { StudentId = student.Id, GuardianId = guardian.Id, IsPrimary = true });

        if (request.BatchId is { } batchId)
        {
            var enrollment = await enrollments.BuildAsync(
                student.Id, batchId, request.FeePlanId, request.DiscountAmount, request.AdmissionDate, ct);
            if (enrollment.IsFailure)
            {
                Discard();
                return Result.Failure<Guid>(enrollment.Errors.ToArray());
            }
            enrollments.AddWithDues(enrollment.Value);   // enrollment + fee dues, saved together below
        }

        await db.SaveChangesAsync(ct);
        return student.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, UpdateStudentRequest request, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure(Error.Forbidden());

        var validation = await updateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Result.Invalid(validation);

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null) return Result.Failure(Error.NotFound("Student"));

        var admissionNo = request.AdmissionNo.Trim();
        if (admissionNo != student.AdmissionNo && await db.Students.AnyAsync(s => s.AdmissionNo == admissionNo && s.Id != id, ct))
        {
            return Result.Failure(Error.Conflict("AdmissionNo.Taken", $"Admission number {admissionNo} is already used."));
        }

        student.AdmissionNo = admissionNo;
        student.FirstName = request.FirstName.Trim();
        student.LastName = Clean(request.LastName);
        student.Gender = Clean(request.Gender);
        student.DateOfBirth = request.DateOfBirth;
        student.Phone = PhoneNumber.Normalize(request.Phone);
        student.SchoolName = Clean(request.SchoolName);
        student.ClassGrade = Clean(request.ClassGrade);
        student.Address = Clean(request.Address);
        student.AdmissionDate = request.AdmissionDate;
        student.Notes = Clean(request.Notes);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Active / Inactive / Left. Marking a student as Left ends their active enrollments.</summary>
    public async Task<Result> SetStatusAsync(Guid id, StudentStatus status, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure(Error.Forbidden());

        var student = await db.Students.Include(s => s.Enrollments).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (student is null) return Result.Failure(Error.NotFound("Student"));

        student.Status = status;
        if (status == StudentStatus.Left)
        {
            foreach (var e in student.Enrollments.Where(e => e.Status == EnrollmentStatus.Active))
            {
                e.Status = EnrollmentStatus.Dropped;
                e.EndedOn = clock.Today();
            }
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Adds another guardian (or links an existing one, e.g. a sibling's parent) to a student.</summary>
    public async Task<Result> AddGuardianAsync(
        Guid studentId, Guid? existingGuardianId, GuardianInput? input, bool makePrimary, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure(Error.Forbidden());

        var student = await db.Students.Include(s => s.Guardians).FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return Result.Failure(Error.NotFound("Student"));

        Guardian guardian;
        if (existingGuardianId is { } gid)
        {
            var existing = await db.Guardians.FirstOrDefaultAsync(g => g.Id == gid, ct);
            if (existing is null) return Result.Failure(Error.NotFound("Guardian"));
            if (student.Guardians.Any(g => g.GuardianId == gid))
                return Result.Failure(Error.Conflict("Guardian.Linked", "This guardian is already linked to the student."));
            guardian = existing;
        }
        else
        {
            if (input is null) return Result.Failure(Error.Validation("Guardian", "Add a parent or guardian."));
            var validation = await guardianValidator.ValidateAsync(input, ct);
            if (!validation.IsValid) return Result.Invalid(validation);
            guardian = NewGuardian(input);
            db.Guardians.Add(guardian);
        }

        var primary = makePrimary || student.Guardians.Count == 0;
        if (primary)
        {
            foreach (var link in student.Guardians) link.IsPrimary = false;
        }
        db.StudentGuardians.Add(new StudentGuardian { StudentId = studentId, GuardianId = guardian.Id, IsPrimary = primary });

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UpdateGuardianAsync(Guid guardianId, GuardianInput input, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure(Error.Forbidden());

        var validation = await guardianValidator.ValidateAsync(input, ct);
        if (!validation.IsValid) return Result.Invalid(validation);

        var guardian = await db.Guardians.FirstOrDefaultAsync(g => g.Id == guardianId, ct);
        if (guardian is null) return Result.Failure(Error.NotFound("Guardian"));

        var optInBefore = guardian.WhatsAppOptIn;
        Apply(guardian, input);
        if (input.WhatsAppOptIn && !optInBefore) guardian.OptInAt = clock.UtcNow;
        if (!input.WhatsAppOptIn) guardian.OptInAt = null;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetPrimaryGuardianAsync(Guid studentId, Guid guardianId, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure(Error.Forbidden());

        var links = await db.StudentGuardians.Where(sg => sg.StudentId == studentId).ToListAsync(ct);
        if (links.All(l => l.GuardianId != guardianId)) return Result.Failure(Error.NotFound("Guardian"));

        foreach (var link in links) link.IsPrimary = link.GuardianId == guardianId;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Next free admission number: receipt prefix + 4 digits, e.g. SSC0042.</summary>
    private async Task<string> NextAdmissionNoAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId;
        var prefix = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.ReceiptPrefix).FirstOrDefaultAsync(ct) ?? "ADM";
        var count = await db.Students.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId, ct);

        for (var n = count + 1; ; n++)
        {
            var candidate = $"{prefix}{n:D4}";
            if (!await db.Students.AnyAsync(s => s.AdmissionNo == candidate, ct)) return candidate;
        }
    }

    private Guardian NewGuardian(GuardianInput input)
    {
        var guardian = new Guardian();
        Apply(guardian, input);
        guardian.OptInAt = input.WhatsAppOptIn ? clock.UtcNow : null;
        return guardian;
    }

    private static void Apply(Guardian guardian, GuardianInput input)
    {
        guardian.FullName = input.FullName.Trim();
        guardian.Relation = input.Relation;
        guardian.Phone = PhoneNumber.Normalize(input.Phone)!;
        guardian.AltPhone = PhoneNumber.Normalize(input.AltPhone);
        guardian.Email = Clean(input.Email);
        guardian.WhatsAppOptIn = input.WhatsAppOptIn;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Forget unsaved changes after a failed admission (Blazor Server keeps one DbContext per circuit).</summary>
    private void Discard()
    {
        if (db is DbContext context) context.ChangeTracker.Clear();
    }
}
