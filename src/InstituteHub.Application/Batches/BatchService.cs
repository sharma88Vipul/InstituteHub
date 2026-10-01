using FluentValidation;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;
using InstituteHub.Application.Users;
using InstituteHub.Domain.Batches;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Application.Batches;

public sealed class BatchService(
    IAppDbContext db,
    ICurrentUser user,
    IUserDirectory users,
    IValidator<BatchRequest> validator)
{
    /// <summary>Teachers see only the batches they teach.</summary>
    private IQueryable<Batch> VisibleBatches()
    {
        var batches = db.Batches.AsNoTracking();
        if (user.IsTeacherOnly())
        {
            var me = user.UserId;
            batches = batches.Where(b => b.TeacherId == me);
        }
        return batches;
    }

    public async Task<IReadOnlyList<BatchListItem>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var query = VisibleBatches();
        if (!includeInactive) query = query.Where(b => b.IsActive);

        var rows = await query
            .OrderByDescending(b => b.IsActive).ThenBy(b => b.Name)
            .Select(b => new
            {
                b.Id, b.Name, b.Subject, b.TeacherId, b.StartTime, b.EndTime, b.DaysOfWeek, b.Capacity, b.IsActive,
                Enrolled = b.Enrollments.Count(e => e.Status == EnrollmentStatus.Active),
            })
            .ToListAsync(ct);

        var teacherNames = await users.GetNamesAsync(
            rows.Where(r => r.TeacherId is not null).Select(r => r.TeacherId!.Value).Distinct(), ct);

        return rows.Select(r => new BatchListItem(
                r.Id, r.Name, r.Subject,
                r.TeacherId is { } t && teacherNames.TryGetValue(t, out var name) ? name : null,
                BatchSchedule.Describe(r.DaysOfWeek, r.StartTime, r.EndTime),
                r.Enrolled, r.Capacity, r.IsActive))
            .ToList();
    }

    public async Task<IReadOnlyList<BatchOption>> GetOptionsAsync(CancellationToken ct = default)
    {
        var rows = await VisibleBatches()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new { b.Id, b.Name, b.DefaultFeePlanId, b.DaysOfWeek, b.StartTime, b.EndTime })
            .ToListAsync(ct);

        return rows
            .Select(r => new BatchOption(r.Id, r.Name, r.DefaultFeePlanId,
                BatchSchedule.Describe(r.DaysOfWeek, r.StartTime, r.EndTime)))
            .ToList();
    }

    public async Task<BatchDetails?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await VisibleBatches().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null) return null;

        var students = await db.Enrollments.AsNoTracking()
            .Where(e => e.BatchId == id && e.Status == EnrollmentStatus.Active)
            .OrderBy(e => e.Student!.FirstName).ThenBy(e => e.Student!.LastName)
            .Select(e => new BatchStudent(
                e.StudentId,
                e.Id,
                e.Student!.AdmissionNo,
                e.Student.FirstName + (e.Student.LastName == null ? "" : " " + e.Student.LastName),
                e.EnrolledOn,
                e.Student.Guardians.OrderByDescending(g => g.IsPrimary).Select(g => g.Guardian!.Phone).FirstOrDefault()))
            .ToListAsync(ct);

        string? feePlanName = batch.DefaultFeePlanId is { } planId
            ? await db.FeePlans.Where(p => p.Id == planId).Select(p => p.Name).FirstOrDefaultAsync(ct)
            : null;

        string? teacherName = null;
        if (batch.TeacherId is { } teacherId)
        {
            (await users.GetNamesAsync([teacherId], ct)).TryGetValue(teacherId, out teacherName);
        }

        return new BatchDetails(batch.Id, batch.Name, batch.Subject, batch.TeacherId, teacherName,
            batch.StartTime, batch.EndTime, batch.DaysOfWeek, batch.Capacity, batch.StartDate, batch.EndDate,
            batch.DefaultFeePlanId, feePlanName, batch.IsActive, students);
    }

    public async Task<Result<Guid>> CreateAsync(BatchRequest request, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure<Guid>(Error.Forbidden());
        var check = await CheckAsync(request, ct);
        if (check.IsFailure) return Result.Failure<Guid>(check.Errors.ToArray());

        var batch = new Batch { IsActive = true };
        Apply(batch, request);
        db.Batches.Add(batch);
        await db.SaveChangesAsync(ct);
        return batch.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, BatchRequest request, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure(Error.Forbidden());
        var check = await CheckAsync(request, ct);
        if (check.IsFailure) return check;

        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null) return Result.Failure(Error.NotFound("Batch"));

        Apply(batch, request);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure(Error.Forbidden());
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null) return Result.Failure(Error.NotFound("Batch"));

        batch.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> CheckAsync(BatchRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return Result.Invalid(validation);

        if (request.TeacherId is { } teacherId &&
            !(await users.GetTeachersAsync(ct)).Any(t => t.Id == teacherId))
        {
            return Result.Failure(Error.Validation(nameof(request.TeacherId), "Choose a teacher from your institute."));
        }

        if (request.DefaultFeePlanId is { } planId && !await db.FeePlans.AnyAsync(p => p.Id == planId && p.IsActive, ct))
        {
            return Result.Failure(Error.Validation(nameof(request.DefaultFeePlanId), "Choose an active fee plan."));
        }

        return Result.Success();
    }

    private static void Apply(Batch batch, BatchRequest r)
    {
        batch.Name = r.Name.Trim();
        batch.Subject = string.IsNullOrWhiteSpace(r.Subject) ? null : r.Subject.Trim();
        batch.TeacherId = r.TeacherId;
        batch.StartTime = r.StartTime;
        batch.EndTime = r.EndTime;
        batch.DaysOfWeek = r.DaysOfWeek;
        batch.Capacity = r.Capacity;
        batch.StartDate = r.StartDate;
        batch.EndDate = r.EndDate;
        batch.DefaultFeePlanId = r.DefaultFeePlanId;
    }
}
