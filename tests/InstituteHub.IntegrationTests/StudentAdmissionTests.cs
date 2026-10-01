using InstituteHub.Application.Batches;
using InstituteHub.Application.Common;
using InstituteHub.Application.Fees;
using InstituteHub.Application.Students;
using InstituteHub.Application.Tenants;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Billing;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Students;
using InstituteHub.Infrastructure.Identity;
using InstituteHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.IntegrationTests;

/// <summary>Week 3 "done when": admit a student with a guardian into a batch.</summary>
[Collection(PostgresCollection.Name)]
public class StudentAdmissionTests(PostgresWebAppFactory factory)
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private async Task<Guid> NewTenantAsync(string name)
    {
        await using var anonymous = new TestScope(factory.Services, tenantId: null);
        var result = await anonymous.Get<ISignUpService>().SignUpAsync(
            new SignUpRequest(name, "Owner", "9876543210", $"o-{Guid.NewGuid():N}@example.com", "Secret#1234"),
            CancellationToken.None);
        return result.Value.TenantId;
    }

    private static async Task<(Guid BatchId, Guid PlanId)> NewBatchAsync(TestScope owner, string name = "Class 12 Physics", Guid? teacherId = null, int? capacity = null)
    {
        var plan = await owner.Get<FeePlanService>().CreateAsync(
            new FeePlanRequest("4 instalments", BillingType.Instalments, 40000, null, 4, 1, 5));
        plan.IsSuccess.ShouldBeTrue();

        var batch = await owner.Get<BatchService>().CreateAsync(new BatchRequest(
            name, "Physics", teacherId, new TimeOnly(17, 0), new TimeOnly(18, 30),
            WeekDays.Monday | WeekDays.Wednesday | WeekDays.Friday, capacity, Today, null, plan.Value));
        batch.IsSuccess.ShouldBeTrue(string.Join("; ", batch.Errors.Select(e => e.Message)));

        return (batch.Value, plan.Value);
    }

    private static AdmissionRequest Admission(string firstName, Guid? batchId, string guardianPhone = "98140 12345", Guid? existingGuardian = null) =>
        new(null, firstName, "Sharma", "Female", null, null, "DAV School", "Class 12", null, Today, null,
            existingGuardian,
            existingGuardian is null ? new GuardianInput("Rakesh Sharma", GuardianRelation.Father, guardianPhone, null, null, true) : null,
            batchId, null, 2000);

    [Fact]
    public async Task Owner_admits_student_with_guardian_into_batch()
    {
        var tenantId = await NewTenantAsync("Admission Test Classes");
        await using var owner = TestScope.AsOwner(factory.Services, tenantId);
        var (batchId, planId) = await NewBatchAsync(owner);

        var admitted = await owner.Get<StudentService>().AdmitAsync(Admission("Priya", batchId));
        admitted.IsSuccess.ShouldBeTrue(string.Join("; ", admitted.Errors.Select(e => e.Message)));

        var details = await owner.Get<StudentService>().GetAsync(admitted.Value);
        details.ShouldNotBeNull();
        details.AdmissionNo.ShouldBe("ATC0001");            // receipt prefix + running number
        details.Guardians.Count.ShouldBe(1);
        details.Guardians[0].Phone.ShouldBe("+919814012345");
        details.Guardians[0].IsPrimary.ShouldBeTrue();
        details.Enrollments.Count.ShouldBe(1);
        details.Enrollments[0].BatchId.ShouldBe(batchId);
        details.Enrollments[0].Status.ShouldBe(EnrollmentStatus.Active);
        details.Enrollments[0].DiscountAmount.ShouldBe(2000);

        var enrollment = await owner.Db.Enrollments.SingleAsync(e => e.StudentId == admitted.Value);
        enrollment.FeePlanId.ShouldBe(planId);              // batch default plan used

        var list = await owner.Get<StudentService>().SearchAsync(new StudentQuery("priya"));
        list.Items.Single().Batches.ShouldBe(new[] { "Class 12 Physics" });
    }

    [Fact]
    public async Task Sibling_shares_the_existing_guardian()
    {
        var tenantId = await NewTenantAsync("Sibling Test Classes");
        await using var owner = TestScope.AsOwner(factory.Services, tenantId);
        var students = owner.Get<StudentService>();

        var first = await students.AdmitAsync(Admission("Aman", null, "9815099999"));
        first.IsSuccess.ShouldBeTrue();

        var matches = await students.FindGuardiansByPhoneAsync("+91 98150 99999");
        matches.Count.ShouldBe(1);
        matches[0].Children.ShouldBe(new[] { "Aman Sharma" });

        var second = await students.AdmitAsync(Admission("Anu", null, existingGuardian: matches[0].Id));
        second.IsSuccess.ShouldBeTrue();

        (await owner.Db.Guardians.CountAsync()).ShouldBe(1);
        var details = await students.GetAsync(second.Value);
        details!.Guardians.Single().OtherChildren.ShouldBe(new[] { "Aman Sharma" });
    }

    [Fact]
    public async Task Plan_student_limit_is_enforced()
    {
        var tenantId = await NewTenantAsync("Tiny Plan Classes");

        await using (var platform = new TestScope(factory.Services, tenantId: null))
        {
            var tiny = new SubscriptionPlan { Code = $"T{Guid.NewGuid():N}"[..20], Name = "Tiny", MaxStudents = 1, Features = "{}" };
            platform.Db.SubscriptionPlans.Add(tiny);
            var subscription = await platform.Db.TenantSubscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenantId);
            subscription.PlanId = tiny.Id;
            await platform.Db.SaveChangesAsync();
        }

        await using var owner = TestScope.AsOwner(factory.Services, tenantId);
        (await owner.Get<StudentService>().AdmitAsync(Admission("One", null, "9815011111"))).IsSuccess.ShouldBeTrue();

        var second = await owner.Get<StudentService>().AdmitAsync(Admission("Two", null, "9815022222"));
        second.IsFailure.ShouldBeTrue();
        second.FirstError!.Type.ShouldBe(ErrorType.LimitReached);
    }

    [Fact]
    public async Task Double_enrollment_and_full_batch_are_rejected()
    {
        var tenantId = await NewTenantAsync("Capacity Classes");
        await using var owner = TestScope.AsOwner(factory.Services, tenantId);
        var (batchId, _) = await NewBatchAsync(owner, capacity: 1);

        var first = await owner.Get<StudentService>().AdmitAsync(Admission("Ravi", batchId, "9815033333"));
        first.IsSuccess.ShouldBeTrue();

        var again = await owner.Get<EnrollmentService>().EnrollAsync(new EnrollRequest(first.Value, batchId, null, 0));
        again.FirstError!.Code.ShouldBe("Enrollment.Exists");

        var full = await owner.Get<StudentService>().AdmitAsync(Admission("Simran", batchId, "9815044444"));
        full.FirstError!.Code.ShouldBe("Batch.Full");
        (await owner.Db.Students.CountAsync()).ShouldBe(1);   // failed admission saved nothing
    }

    [Fact]
    public async Task Teacher_sees_only_own_batches_and_cannot_admit()
    {
        var tenantId = await NewTenantAsync("Teacher Scope Classes");

        Guid teacherId;
        await using (var platform = new TestScope(factory.Services, tenantId: null))
        {
            var users = platform.Get<UserManager<AppUser>>();
            var teacher = new AppUser
            {
                UserName = $"t-{Guid.NewGuid():N}@example.com", Email = $"t-{Guid.NewGuid():N}@example.com",
                FullName = "Mr Teacher", TenantId = tenantId, EmailConfirmed = true,
            };
            (await users.CreateAsync(teacher, "Secret#1234")).Succeeded.ShouldBeTrue();
            await users.AddToRoleAsync(teacher, Roles.Teacher);
            teacherId = teacher.Id;
        }

        await using (var owner = TestScope.AsOwner(factory.Services, tenantId))
        {
            var (mine, _) = await NewBatchAsync(owner, "Teacher's batch", teacherId);
            var (other, _) = await NewBatchAsync(owner, "Someone else's batch");
            (await owner.Get<StudentService>().AdmitAsync(Admission("Mine", mine, "9815055555"))).IsSuccess.ShouldBeTrue();
            (await owner.Get<StudentService>().AdmitAsync(Admission("Other", other, "9815066666"))).IsSuccess.ShouldBeTrue();
        }

        await using var asTeacher = new TestScope(factory.Services, tenantId, teacherId, Roles.Teacher);
        var visible = await asTeacher.Get<StudentService>().SearchAsync(new StudentQuery());
        visible.Items.Select(s => s.FullName).ShouldBe(new[] { "Mine Sharma" });
        (await asTeacher.Get<BatchService>().ListAsync()).Select(b => b.Name).ShouldBe(new[] { "Teacher's batch" });

        var admit = await asTeacher.Get<StudentService>().AdmitAsync(Admission("Nope", null, "9815077777"));
        admit.FirstError!.Type.ShouldBe(ErrorType.Forbidden);
    }
}
