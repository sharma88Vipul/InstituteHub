using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Batches;
using InstituteHub.Application.Common;
using InstituteHub.Application.Fees;
using InstituteHub.Application.Students;
using InstituteHub.Application.Tenants;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Students;
using InstituteHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.IntegrationTests;

/// <summary>Week 5: dues are created with the enrollment and shown on the student's Fees tab.</summary>
[Collection(PostgresCollection.Name)]
public class FeeDueTests(PostgresWebAppFactory factory)
{
    private async Task<SignUpResult> NewTenantAsync(string name)
    {
        await using var anonymous = new TestScope(factory.Services, tenantId: null);
        return (await anonymous.Get<ISignUpService>().SignUpAsync(
            new SignUpRequest(name, "Owner", "9876543210", $"o-{Guid.NewGuid():N}@example.com", "Secret#1234"),
            CancellationToken.None)).Value;
    }

    private static async Task<Guid> BatchWithPlanAsync(TestScope owner, FeePlanRequest planRequest)
    {
        var plan = await owner.Get<FeePlanService>().CreateAsync(planRequest);
        plan.IsSuccess.ShouldBeTrue();
        var today = owner.Get<IClock>().Today();
        var batch = await owner.Get<BatchService>().CreateAsync(new BatchRequest(
            $"Batch {planRequest.Name}", null, null, null, null, (WeekDays)127, null, today, null, plan.Value));
        batch.IsSuccess.ShouldBeTrue();
        return batch.Value;
    }

    private static async Task<Guid> AdmitAsync(TestScope owner, Guid batchId, decimal discount, string phone)
    {
        var today = owner.Get<IClock>().Today();
        var result = await owner.Get<StudentService>().AdmitAsync(new AdmissionRequest(
            null, "Student", null, null, null, null, null, null, null, today, null, null,
            new GuardianInput("Parent", GuardianRelation.Father, phone, null, null, true), batchId, null, discount));
        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        return result.Value;
    }

    [Fact]
    public async Task Admission_creates_dues_for_each_billing_type()
    {
        var tenant = await NewTenantAsync("Dues Classes");
        await using var owner = new TestScope(factory.Services, tenant.TenantId, tenant.UserId, Roles.Owner);

        var oneTime = await AdmitAsync(owner, await BatchWithPlanAsync(owner, new("Crash course", BillingType.OneTime, 15000, null, null, null, 5)), 1000, "9815200001");
        var instalments = await AdmitAsync(owner, await BatchWithPlanAsync(owner, new("JEE 4 inst", BillingType.Instalments, 40000, null, 4, 1, 5)), 2000, "9815200002");
        var monthly = await AdmitAsync(owner, await BatchWithPlanAsync(owner, new("Monthly", BillingType.Monthly, 0, 2500, null, null, 5)), 0, "9815200003");

        var fees = owner.Get<FeeDueService>();

        var a = (await fees.GetStudentFeesAsync(oneTime))!;
        a.Dues.Count.ShouldBe(1);
        a.Totals.Balance.ShouldBe(14000);

        var b = (await fees.GetStudentFeesAsync(instalments))!;
        b.Dues.Count.ShouldBe(4);
        b.Totals.Balance.ShouldBe(38000);
        b.Dues.Last().DiscountAmount.ShouldBe(2000);    // discount off the last instalment

        var c = (await fees.GetStudentFeesAsync(monthly))!;
        c.Dues.Single().Amount.ShouldBe(2500);

        // Running the generator again creates nothing new (idempotent).
        var again = await fees.GenerateMissingDuesAsync();
        again.Value.DuesCreated.ShouldBe(0);
        (await owner.Db.FeeDues.CountAsync()).ShouldBe(6);
    }

    [Fact]
    public async Task Missing_dues_are_created_for_older_enrollments_and_monthly_catch_up()
    {
        var tenant = await NewTenantAsync("Backfill Classes");
        Guid monthlyEnrollment;
        await using (var owner = new TestScope(factory.Services, tenant.TenantId, tenant.UserId, Roles.Owner))
        {
            var batchId = await BatchWithPlanAsync(owner, new("Monthly", BillingType.Monthly, 0, 2000, null, null, 5));
            var studentId = await AdmitAsync(owner, batchId, 0, "9815300001");
            var enrollment = await owner.Db.Enrollments.SingleAsync(e => e.StudentId == studentId);
            monthlyEnrollment = enrollment.Id;

            // Pretend the student joined three months ago and only the first month was ever created.
            var today = owner.Get<IClock>().Today();
            enrollment.EnrolledOn = today.AddMonths(-3);
            await owner.Db.SaveChangesAsync();
            var due = await owner.Db.FeeDues.SingleAsync();
            due.PeriodKey = FeeDueGenerator.PeriodKey(today.AddMonths(-3));
            await owner.Db.SaveChangesAsync();
        }

        await using (var owner = new TestScope(factory.Services, tenant.TenantId, tenant.UserId, Roles.Owner))
        {
            var result = await owner.Get<FeeDueService>().GenerateMissingDuesAsync();
            result.Value.DuesCreated.ShouldBe(3);   // the three later months
            (await owner.Db.FeeDues.CountAsync(d => d.EnrollmentId == monthlyEnrollment)).ShouldBe(4);
        }
    }

    [Fact]
    public async Task Owner_can_waive_an_unpaid_due_but_staff_cannot()
    {
        var tenant = await NewTenantAsync("Waive Classes");
        Guid dueId;
        await using (var owner = new TestScope(factory.Services, tenant.TenantId, tenant.UserId, Roles.Owner))
        {
            var studentId = await AdmitAsync(owner, await BatchWithPlanAsync(owner, new("One", BillingType.OneTime, 5000, null, null, null, 5)), 0, "9815400001");
            dueId = (await owner.Get<FeeDueService>().GetStudentFeesAsync(studentId))!.Dues.Single().Id;
        }

        await using (var staff = new TestScope(factory.Services, tenant.TenantId, null, Roles.Staff))
        {
            (await staff.Get<FeeDueService>().WaiveAsync(dueId)).FirstError!.Type.ShouldBe(ErrorType.Forbidden);
        }

        await using var asOwner = new TestScope(factory.Services, tenant.TenantId, tenant.UserId, Roles.Owner);
        (await asOwner.Get<FeeDueService>().WaiveAsync(dueId)).IsSuccess.ShouldBeTrue();
        (await asOwner.Db.FeeDues.SingleAsync(d => d.Id == dueId)).Status.ShouldBe(FeeDueStatus.Waived);
    }

    [Fact]
    public async Task Discount_above_the_fee_is_rejected()
    {
        var tenant = await NewTenantAsync("Discount Classes");
        await using var owner = new TestScope(factory.Services, tenant.TenantId, tenant.UserId, Roles.Owner);
        var batchId = await BatchWithPlanAsync(owner, new("Small", BillingType.OneTime, 3000, null, null, null, 5));
        var today = owner.Get<IClock>().Today();

        var result = await owner.Get<StudentService>().AdmitAsync(new AdmissionRequest(
            null, "Too", null, null, null, null, null, null, null, today, null, null,
            new GuardianInput("Parent", GuardianRelation.Father, "9815500001", null, null, true), batchId, null, 5000));

        result.FirstError!.Code.ShouldBe("DiscountAmount");
        (await owner.Db.Students.CountAsync()).ShouldBe(0);
    }
}
