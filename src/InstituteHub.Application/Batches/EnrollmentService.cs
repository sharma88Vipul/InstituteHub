using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Students;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Application.Batches;

public sealed record EnrollRequest(
    Guid StudentId,
    Guid BatchId,
    Guid? FeePlanId,
    decimal DiscountAmount,
    DateOnly? EnrolledOn = null);

/// <summary>Puts students into batches and takes them out again.</summary>
public sealed class EnrollmentService(IAppDbContext db, ICurrentUser user, IClock clock)
{
    public async Task<Result<Guid>> EnrollAsync(EnrollRequest request, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure<Guid>(Error.Forbidden());

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == request.StudentId, ct);
        if (student is null) return Result.Failure<Guid>(Error.NotFound("Student"));
        if (student.Status != StudentStatus.Active)
            return Result.Failure<Guid>(Error.Conflict("Student.NotActive", "Only active students can join a batch."));

        var built = await BuildAsync(student.Id, request.BatchId, request.FeePlanId, request.DiscountAmount,
            request.EnrolledOn ?? clock.Today(), ct);
        if (built.IsFailure) return Result.Failure<Guid>(built.Errors.ToArray());

        db.Enrollments.Add(built.Value);
        await db.SaveChangesAsync(ct);
        return built.Value.Id;
    }

    /// <summary>Ends an enrollment (student leaves the batch). History and fee dues are kept.</summary>
    public async Task<Result> DropAsync(Guid enrollmentId, DateOnly? endedOn = null, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure(Error.Forbidden());

        var enrollment = await db.Enrollments.FirstOrDefaultAsync(e => e.Id == enrollmentId, ct);
        if (enrollment is null) return Result.Failure(Error.NotFound("Enrollment"));
        if (enrollment.Status != EnrollmentStatus.Active) return Result.Success();

        enrollment.Status = EnrollmentStatus.Dropped;
        enrollment.EndedOn = endedOn ?? clock.Today();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Validates and builds (but does not save) an enrollment: batch is active and not full, the student is not
    /// already in it, and a fee plan is chosen (the batch's default plan when none is given).
    /// Fee dues are generated from it in week 5 (FeeDueGenerator).
    /// </summary>
    internal async Task<Result<Enrollment>> BuildAsync(
        Guid studentId, Guid batchId, Guid? feePlanId, decimal discount, DateOnly enrolledOn, CancellationToken ct)
    {
        var batch = await db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null || !batch.IsActive)
            return Result.Failure<Enrollment>(Error.Validation("BatchId", "Choose an active batch."));

        var planId = feePlanId ?? batch.DefaultFeePlanId;
        if (planId is null)
            return Result.Failure<Enrollment>(Error.Validation("FeePlanId", "Choose a fee plan for this batch."));

        if (!await db.FeePlans.AnyAsync(p => p.Id == planId && p.IsActive, ct))
            return Result.Failure<Enrollment>(Error.Validation("FeePlanId", "Choose an active fee plan."));

        if (discount < 0)
            return Result.Failure<Enrollment>(Error.Validation("DiscountAmount", "The discount cannot be negative."));

        if (await db.Enrollments.AnyAsync(e => e.StudentId == studentId && e.BatchId == batchId
                                               && e.Status == EnrollmentStatus.Active, ct))
            return Result.Failure<Enrollment>(Error.Conflict("Enrollment.Exists", "This student is already in this batch."));

        if (batch.Capacity is { } capacity &&
            await db.Enrollments.CountAsync(e => e.BatchId == batchId && e.Status == EnrollmentStatus.Active, ct) >= capacity)
            return Result.Failure<Enrollment>(Error.Conflict("Batch.Full", $"{batch.Name} is full ({capacity} students)."));

        return new Enrollment
        {
            StudentId = studentId,
            BatchId = batchId,
            FeePlanId = planId.Value,
            DiscountAmount = discount,
            EnrolledOn = enrolledOn,
            Status = EnrollmentStatus.Active,
        };
    }
}
