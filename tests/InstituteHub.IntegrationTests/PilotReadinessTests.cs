using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Attendance;
using InstituteHub.Application.Batches;
using InstituteHub.Application.Billing;
using InstituteHub.Application.Common;
using InstituteHub.Application.Fees;
using InstituteHub.Application.Payments;
using InstituteHub.Application.Reports;
using InstituteHub.Application.Students;
using InstituteHub.Application.Tenants;
using InstituteHub.Application.Users;
using InstituteHub.Domain.Attendance;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Billing;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Students;
using InstituteHub.Infrastructure.Identity;
using InstituteHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.IntegrationTests;

/// <summary>Week 8 "done when": a pilot institute can be onboarded (import, limits, reports, users, settings).</summary>
[Collection(PostgresCollection.Name)]
public class PilotReadinessTests(PostgresWebAppFactory factory)
{
    private sealed record Setup(Guid TenantId, Guid OwnerId, Guid BatchId, string BatchName, DateOnly Today);

    /// <summary>A new institute with a ₹1,000/month batch and no students.</summary>
    private async Task<Setup> ArrangeAsync(string institute)
    {
        SignUpResult signUp;
        await using (var anonymous = new TestScope(factory.Services, tenantId: null))
        {
            signUp = (await anonymous.Get<ISignUpService>().SignUpAsync(
                new SignUpRequest(institute, "Owner", "9876543210", $"o-{Guid.NewGuid():N}@example.com", "Secret#1234"),
                CancellationToken.None)).Value;
        }

        await using var owner = AsOwner(signUp.TenantId, signUp.UserId);
        var today = owner.Get<IClock>().Today();
        var plan = await owner.Get<FeePlanService>().CreateAsync(new FeePlanRequest("Monthly", BillingType.Monthly, 0, 1000, null, null, 5));
        plan.IsSuccess.ShouldBeTrue(string.Join("; ", plan.Errors.Select(e => e.Message)));
        var batchName = "Class 10 Maths";
        var batch = await owner.Get<BatchService>().CreateAsync(new BatchRequest(
            batchName, null, null, null, null, (WeekDays)127, null, today.AddMonths(-1), null, plan.Value));
        batch.IsSuccess.ShouldBeTrue(string.Join("; ", batch.Errors.Select(e => e.Message)));
        return new Setup(signUp.TenantId, signUp.UserId, batch.Value, batchName, today);
    }

    private TestScope AsOwner(Guid tenantId, Guid userId) => new(factory.Services, tenantId, userId, Roles.Owner);
    private TestScope AsOwner(Setup s) => AsOwner(s.TenantId, s.OwnerId);

    private static string Csv(params string[] rows) =>
        "AdmissionNo,FirstName,LastName,GuardianName,GuardianPhone,WhatsAppConsent,Batch\n" + string.Join("\n", rows) + "\n";

    [Fact]
    public async Task Csv_import_admits_students_links_siblings_and_creates_dues()
    {
        var s = await ArrangeAsync("Import Classes");
        await using var owner = AsOwner(s);
        var importer = owner.Get<StudentImportService>();
        var csv = Csv(
            $",Aarav,Sharma,Rakesh Sharma,9815611111,Yes,{s.BatchName}",
            $",Diya,Sharma,Rakesh Sharma,98156 11111,Yes,{s.BatchName}",
            ",Kabir,,Meena,12,No,");

        var preview = (await importer.PreviewAsync(csv)).Value;
        preview.ValidCount.ShouldBe(2);
        preview.ErrorCount.ShouldBe(1);
        preview.CanImport.ShouldBeTrue();

        var result = await importer.ImportAsync(csv);
        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        result.Value.Imported.ShouldBe(2);
        result.Value.Failed.ShouldHaveSingleItem().LineNumber.ShouldBe(4);

        (await owner.Db.Students.CountAsync()).ShouldBe(2);
        (await owner.Db.Guardians.CountAsync()).ShouldBe(1); // siblings share one guardian
        (await owner.Db.Enrollments.CountAsync(e => e.BatchId == s.BatchId)).ShouldBe(2);
        (await owner.Db.FeeDues.CountAsync()).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Plan_limit_blocks_admission_and_import()
    {
        var s = await ArrangeAsync("Tiny Plan Classes");
        await using (var owner = AsOwner(s))
        {
            // A one-student paid plan for this institute only.
            var tiny = new SubscriptionPlan { Code = $"T{Guid.NewGuid():N}"[..20], Name = "Tiny", MaxStudents = 1, Features = "{}" };
            owner.Db.SubscriptionPlans.Add(tiny);
            var subscription = await owner.Db.TenantSubscriptions.SingleAsync();
            subscription.PlanId = tiny.Id;
            await owner.Db.SaveChangesAsync();
            owner.Get<IFeatureService>().Invalidate();
        }

        await using var scope = AsOwner(s);
        var first = await scope.Get<StudentImportService>().ImportAsync(Csv(",One,,Parent One,9815622221,No,"));
        first.IsSuccess.ShouldBeTrue(string.Join("; ", first.Errors.Select(e => e.Message)));

        var preview = (await scope.Get<StudentImportService>().PreviewAsync(Csv(",Two,,Parent Two,9815622222,No,"))).Value;
        preview.LimitWarning.ShouldNotBeNull().ShouldContain("1 active students");
        preview.CanImport.ShouldBeFalse();

        var admit = await scope.Get<StudentService>().AdmitAsync(new AdmissionRequest(
            null, "Three", null, null, null, null, null, null, null, s.Today, null, null,
            new GuardianInput("Parent Three", GuardianRelation.Mother, "9815622223", null, null, false), null, null, 0));
        admit.IsFailure.ShouldBeTrue();
        admit.FirstError!.Type.ShouldBe(ErrorType.LimitReached);
    }

    [Fact]
    public async Task Reports_show_collections_dues_and_attendance()
    {
        var s = await ArrangeAsync("Report Classes");
        await using var owner = AsOwner(s);
        (await owner.Get<StudentImportService>().ImportAsync(Csv(
            $"R1,Aarav,,Parent A,9815633331,No,{s.BatchName}",
            $"R2,Diya,,Parent B,9815633332,No,{s.BatchName}"))).Value.Imported.ShouldBe(2);
        var aarav = await owner.Db.Students.SingleAsync(x => x.AdmissionNo == "R1");
        var diya = await owner.Db.Students.SingleAsync(x => x.AdmissionNo == "R2");

        var pay = await owner.Get<PaymentService>().RecordAsync(new RecordPaymentRequest(aarav.Id, s.Today, 1000, PaymentMode.UPI, "UPI1", null, null));
        pay.IsSuccess.ShouldBeTrue(string.Join("; ", pay.Errors.Select(e => e.Message)));
        (await owner.Get<AttendanceService>().SaveAsync(new SaveAttendanceRequest(s.BatchId, s.Today,
            [new AttendanceMark(aarav.Id, AttendanceStatus.Present), new AttendanceMark(diya.Id, AttendanceStatus.Absent)]))).IsSuccess.ShouldBeTrue();

        var reports = owner.Get<ReportService>();
        var monthStart = new DateOnly(s.Today.Year, s.Today.Month, 1);

        var collections = (await reports.GetCollectionsAsync(monthStart, s.Today))!;
        collections.Total.ShouldBe(1000);
        collections.ByMode.ShouldHaveSingleItem().Mode.ShouldBe(PaymentMode.UPI);
        collections.ByBatch.ShouldHaveSingleItem().Batch.ShouldBe(s.BatchName);

        var dues = (await reports.GetDuesAgeingAsync())!;
        dues.Students.ShouldHaveSingleItem().StudentId.ShouldBe(diya.Id);
        dues.Total.ShouldBe(1000);

        var attendance = (await reports.GetAttendanceAsync(monthStart, s.Today))!;
        attendance.Batches.ShouldHaveSingleItem().Summary.Percentage.ShouldBe(50);
        attendance.LowAttendance.ShouldHaveSingleItem().StudentId.ShouldBe(diya.Id);

        var csv = System.Text.Encoding.UTF8.GetString((await reports.CollectionsCsvAsync(monthStart, s.Today))!);
        csv.ShouldContain("R1");
    }

    [Fact]
    public async Task Owner_adds_and_deactivates_a_teacher_and_teachers_cannot_see_money_reports()
    {
        var s = await ArrangeAsync("Team Classes");
        Guid teacherId;
        await using (var owner = AsOwner(s))
        {
            var created = await owner.Get<IUserManagement>().CreateAsync(
                new NewUserRequest("Ravi Teacher", $"t-{Guid.NewGuid():N}@example.com", "9815644441", Roles.Teacher, "Teach#2026x"));
            created.IsSuccess.ShouldBeTrue(string.Join("; ", created.Errors.Select(e => e.Message)));
            teacherId = created.Value;

            var list = await owner.Get<IUserManagement>().ListAsync();
            list.Count.ShouldBe(2);
            list.Single(u => u.Id == teacherId).Role.ShouldBe(Roles.Teacher);
            (await owner.Get<TenantService>().GetOnboardingChecklistAsync())!.HasTeam.ShouldBeTrue();

            (await owner.Get<IUserManagement>().SetActiveAsync(teacherId, false)).IsSuccess.ShouldBeTrue();
            (await owner.Get<IUserManagement>().SetActiveAsync(s.OwnerId, false)).IsFailure.ShouldBeTrue();
        }

        await using (var check = new TestScope(factory.Services, s.TenantId))
        {
            var users = check.Get<UserManager<AppUser>>();
            var teacher = (await users.FindByIdAsync(teacherId.ToString()))!;
            teacher.IsActive.ShouldBeFalse();
            (await users.IsLockedOutAsync(teacher)).ShouldBeTrue();
            (await users.CheckPasswordAsync(teacher, "Teach#2026x")).ShouldBeTrue();
        }

        await using var asTeacher = new TestScope(factory.Services, s.TenantId, teacherId, Roles.Teacher);
        (await asTeacher.Get<ReportService>().GetCollectionsAsync(s.Today, s.Today)).ShouldBeNull();
        (await asTeacher.Get<IUserManagement>().CreateAsync(
            new NewUserRequest("X", "x@example.com", null, Roles.Staff, "Secret#1234"))).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Owner_updates_institute_profile_and_receipt_prefix()
    {
        var s = await ArrangeAsync("Profile Classes");
        await using var owner = AsOwner(s);
        var tenants = owner.Get<TenantService>();

        var bad = await tenants.UpdateProfileAsync(new InstituteProfileRequest(
            "Profile Classes", "Owner", "9876543210", null, null, null, null, null, null, "A/B"));
        bad.IsFailure.ShouldBeTrue();

        var ok = await tenants.UpdateProfileAsync(new InstituteProfileRequest(
            "Profile Classes Ludhiana", "Neha Sharma", "9876543210", "office@example.com", "12 Model Town", "Ludhiana",
            "Punjab", "141002", null, "pcl"));
        ok.IsSuccess.ShouldBeTrue(string.Join("; ", ok.Errors.Select(e => e.Message)));

        var profile = (await tenants.GetProfileAsync())!;
        profile.Name.ShouldBe("Profile Classes Ludhiana");
        profile.ReceiptPrefix.ShouldBe("PCL");
        profile.Phone.ShouldBe("+919876543210");

        await using var asStaff = new TestScope(factory.Services, s.TenantId, Guid.CreateVersion7(), Roles.Staff);
        (await asStaff.Get<TenantService>().UpdateProfileAsync(new InstituteProfileRequest(
            "Hacked", "X", "9876543210", null, null, null, null, null, null, "HX"))).IsFailure.ShouldBeTrue();
    }
}
