using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;
using InstituteHub.Application.Fees;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Students;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Application.Batches;

public sealed record EnrollRequest(
    Guid StudentId,
    Guid BatchId,
    Guid? FeePlanId,
    decimal DiscountAmount,
    DateOnly? EnrolledOn = null);

/// <summary>A validated, not yet saved enrollment and the plan its dues come from.</summary>
internal sealed record BuiltEnrollment(Enrollment Enrollment, FeePlan Plan);

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

        AddWithDues(built.Value);
        await db.SaveChangesAsync(ct);
        return built.Value.Enrollment.Id;
    }

    /// <summary>
    /// Adds the enrollment and its fee dues (design doc 6.3) to the context, so both are saved in the same
    /// SaveChanges. Dues are added explicitly with db.FeeDues.Add: keys are set in C#, so EF would treat an entity
    /// reached only through a navigation as an existing row.
    /// </summary>
    internal IReadOnlyList<FeeDue> AddWithDues(BuiltEnrollment built)
    {
        db.Enrollments.Add(built.Enrollment);
        var dues = FeeDueGenerator.ForNewEnrollment(built.Enrollment, built.Plan, clock.Today());
        foreach (var due in dues) db.FeeDues.Add(due);
        return dues;
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
    /// Use <see cref="AddWithDues"/> to add it together with its fee dues.
    /// </summary>
    internal async Task<Result<BuiltEnrollment>> BuildAsync(
        Guid studentId, Guid batchId, Guid? feePlanId, decimal discount, DateOnly enrolledOn, CancellationToken ct)
    {
        var batch = await db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null || !batch.IsActive)
            return Result.Failure<BuiltEnrollment>(Error.Validation("BatchId", "Choose an active batch."));

        var planId = feePlanId ?? batch.DefaultFeePlanId;
        if (planId is null)
            return Result.Failure<BuiltEnrollment>(Error.Validation("FeePlanId", "Choose a fee plan for this batch."));

        var plan = await db.FeePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId && p.IsActive, ct);
        if (plan is null)
            return Result.Failure<BuiltEnrollment>(Error.Validation("FeePlanId", "Choose an active fee plan."));

        if (discount < 0)
            return Result.Failure<BuiltEnrollment>(Error.Validation("DiscountAmount", "The discount cannot be negative."));

        var maxDiscount = plan.BillingType == BillingType.Monthly ? plan.MonthlyAmount ?? 0 : plan.TotalAmount;
        if (discount > maxDiscount)
            return Result.Failure<BuiltEnrollment>(Error.Validation("DiscountAmount", plan.BillingType == BillingType.Monthly
                ? $"The monthly discount cannot be more than the monthly fee ({Money.Format(maxDiscount)})."
                : $"The discount cannot be more than the total fee ({Money.Format(maxDiscount)})."));

        if (await db.Enrollments.AnyAsync(e => e.StudentId == studentId && e.BatchId == batchId
                                               && e.Status == EnrollmentStatus.Active, ct))
            return Result.Failure<BuiltEnrollment>(Error.Conflict("Enrollment.Exists", "This student is already in this batch."));

        if (batch.Capacity is { } capacity &&
            await db.Enrollments.CountAsync(e => e.BatchId == batchId && e.Status == EnrollmentStatus.Active, ct) >= capacity)
            return Result.Failure<BuiltEnrollment>(Error.Conflict("Batch.Full", $"{batch.Name} is full ({capacity} students)."));

        var enrollment = new Enrollment
        {
            StudentId = studentId,
            BatchId = batchId,
            FeePlanId = plan.Id,
            DiscountAmount = discount,
            EnrolledOn = enrolledOn,
            Status = EnrollmentStatus.Active,
        };
        return new BuiltEnrollment(enrollment, plan);
    }
}
