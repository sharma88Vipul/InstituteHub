using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Attendance;
using InstituteHub.Application.Batches;
using InstituteHub.Application.Common;
using InstituteHub.Application.Fees;
using InstituteHub.Application.Students;
using InstituteHub.Application.Tenants;
using InstituteHub.Domain.Attendance;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Students;
using InstituteHub.Infrastructure.Identity;
using InstituteHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.IntegrationTests;

/// <summary>Week 4 "done when": a teacher marks attendance (on a phone).</summary>
[Collection(PostgresCollection.Name)]
public class AttendanceTests(PostgresWebAppFactory factory)
{
    private const WeekDays EveryDay = (WeekDays)127;

    private sealed record Setup(Guid TenantId, Guid OwnerId, Guid BatchId, Guid TeacherId, DateOnly Today, List<Guid> Students);

    /// <summary>Institute with a teacher, one batch taught by them and three students who joined 30 days ago.</summary>
    private async Task<Setup> ArrangeAsync(string institute)
    {
        SignUpResult signUp;
        await using (var anonymous = new TestScope(factory.Services, tenantId: null))
        {
            signUp = (await anonymous.Get<ISignUpService>().SignUpAsync(
                new SignUpRequest(institute, "Owner", "9876543210", $"o-{Guid.NewGuid():N}@example.com", "Secret#1234"),
                CancellationToken.None)).Value;

            var teacher = new AppUser
            {
                UserName = $"t-{Guid.NewGuid():N}@example.com", Email = $"t-{Guid.NewGuid():N}@example.com",
                FullName = "Ms Teacher", TenantId = signUp.TenantId, EmailConfirmed = true,
            };
            var users = anonymous.Get<UserManager<AppUser>>();
            (await users.CreateAsync(teacher, "Secret#1234")).Succeeded.ShouldBeTrue();
            await users.AddToRoleAsync(teacher, Roles.Teacher);
            _teacherId = teacher.Id;
        }

        await using var owner = new TestScope(factory.Services, signUp.TenantId, signUp.UserId, Roles.Owner);
        var today = owner.Get<IClock>().Today();

        var plan = await owner.Get<FeePlanService>().CreateAsync(new FeePlanRequest("Monthly", BillingType.Monthly, 0, 2500, null, null, 5));
        var batch = await owner.Get<BatchService>().CreateAsync(new BatchRequest(
            "Class 11 Maths", "Maths", _teacherId, new TimeOnly(16, 0), new TimeOnly(17, 0), EveryDay, null,
            today.AddDays(-60), null, plan.Value));
        batch.IsSuccess.ShouldBeTrue(string.Join("; ", batch.Errors.Select(e => e.Message)));

        var students = new List<Guid>();
        foreach (var (name, phone) in new[] { ("Aarav", "9815100001"), ("Bhavna", "9815100002"), ("Chirag", "9815100003") })
        {
            var admitted = await owner.Get<StudentService>().AdmitAsync(new AdmissionRequest(
                null, name, null, null, null, null, null, null, null, today.AddDays(-30), null, null,
                new GuardianInput("Parent " + name, GuardianRelation.Mother, phone, null, null, true),
                batch.Value, null, 0));
            admitted.IsSuccess.ShouldBeTrue(string.Join("; ", admitted.Errors.Select(e => e.Message)));
            students.Add(admitted.Value);
        }

        return new Setup(signUp.TenantId, signUp.UserId, batch.Value, _teacherId, today, students);
    }

    private Guid _teacherId;

    private TestScope AsOwner(Setup s) => new(factory.Services, s.TenantId, s.OwnerId, Roles.Owner);
    private TestScope AsTeacher(Setup s) => new(factory.Services, s.TenantId, s.TeacherId, Roles.Teacher);

    private static List<AttendanceMark> Marks(AttendanceSheet sheet, Guid? absent = null) =>
        sheet.Rows.Select(r => new AttendanceMark(r.StudentId, r.StudentId == absent ? AttendanceStatus.Absent : AttendanceStatus.Present)).ToList();

    [Fact]
    public async Task Teacher_marks_attendance_and_can_correct_it()
    {
        var s = await ArrangeAsync("Register Classes");
        await using var teacher = AsTeacher(s);
        var service = teacher.Get<AttendanceService>();

        var sheet = await service.GetSheetAsync(s.BatchId, s.Today);
        sheet.ShouldNotBeNull();
        sheet.CanEdit.ShouldBeTrue();
        sheet.AlreadyMarked.ShouldBeFalse();
        sheet.Rows.Count.ShouldBe(3);
        sheet.Rows.ShouldAllBe(r => r.Status == AttendanceStatus.Present);   // all present by default

        var saved = await service.SaveAsync(new SaveAttendanceRequest(s.BatchId, s.Today, Marks(sheet, absent: s.Students[1])));
        saved.IsSuccess.ShouldBeTrue(string.Join("; ", saved.Errors.Select(e => e.Message)));
        saved.Value.Absent.ShouldBe(1);

        // Correct it: Bhavna was actually present, Chirag absent.
        var again = await service.SaveAsync(new SaveAttendanceRequest(s.BatchId, s.Today, Marks(sheet, absent: s.Students[2])));
        again.IsSuccess.ShouldBeTrue();

        await using var owner = AsOwner(s);
        (await owner.Db.AttendanceSessions.CountAsync(x => x.BatchId == s.BatchId)).ShouldBe(1);   // upsert, no duplicate register
        (await owner.Db.AttendanceRecords.CountAsync()).ShouldBe(3);

        var reloaded = await owner.Get<AttendanceService>().GetSheetAsync(s.BatchId, s.Today);
        reloaded!.AlreadyMarked.ShouldBeTrue();
        reloaded.MarkedByName.ShouldBe("Ms Teacher");
        reloaded.Rows.Single(r => r.Status == AttendanceStatus.Absent).StudentId.ShouldBe(s.Students[2]);
    }

    [Fact]
    public async Task Student_and_batch_history_show_percentages()
    {
        var s = await ArrangeAsync("History Classes");
        await using var owner = AsOwner(s);
        var service = owner.Get<AttendanceService>();

        for (var daysAgo = 1; daysAgo <= 4; daysAgo++)
        {
            var date = s.Today.AddDays(-daysAgo);
            var sheet = (await service.GetSheetAsync(s.BatchId, date))!;
            // Aarav is absent on 1 of 4 days.
            var result = await service.SaveAsync(new SaveAttendanceRequest(s.BatchId, date, Marks(sheet, daysAgo == 2 ? s.Students[0] : null)));
            result.IsSuccess.ShouldBeTrue();
        }

        var aarav = await service.GetStudentAttendanceAsync(s.Students[0]);
        aarav.Summary.Present.ShouldBe(3);
        aarav.Summary.Absent.ShouldBe(1);
        aarav.Summary.Percentage.ShouldBe(75.0);
        aarav.Recent.First().Date.ShouldBe(s.Today.AddDays(-1));

        var batch = await service.GetBatchAttendanceAsync(s.BatchId, s.Today.AddDays(-10), s.Today);
        batch!.Sessions.Count.ShouldBe(4);
        batch.Students.First().StudentId.ShouldBe(s.Students[0]);    // lowest attendance first
    }

    [Fact]
    public async Task Future_dates_and_old_dates_for_teachers_are_blocked()
    {
        var s = await ArrangeAsync("Rules Classes");

        await using (var teacher = AsTeacher(s))
        {
            var service = teacher.Get<AttendanceService>();
            var old = s.Today.AddDays(-(AttendanceService.TeacherEditDays + 1));

            var oldSheet = (await service.GetSheetAsync(s.BatchId, old))!;
            oldSheet.CanEdit.ShouldBeFalse();
            (await service.SaveAsync(new SaveAttendanceRequest(s.BatchId, old, Marks(oldSheet)))).IsFailure.ShouldBeTrue();

            var tomorrow = s.Today.AddDays(1);
            var futureSheet = (await service.GetSheetAsync(s.BatchId, tomorrow))!;
            (await service.SaveAsync(new SaveAttendanceRequest(s.BatchId, tomorrow, Marks(futureSheet)))).IsFailure.ShouldBeTrue();
        }

        await using var owner = AsOwner(s);
        var ownerService = owner.Get<AttendanceService>();
        var oldDate = s.Today.AddDays(-(AttendanceService.TeacherEditDays + 1));
        var sheet = (await ownerService.GetSheetAsync(s.BatchId, oldDate))!;
        sheet.CanEdit.ShouldBeTrue();                                  // the office can correct older dates
        (await ownerService.SaveAsync(new SaveAttendanceRequest(s.BatchId, oldDate, Marks(sheet)))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Register_lists_only_students_enrolled_on_that_date_and_teacher_sees_only_own_batch()
    {
        var s = await ArrangeAsync("Roster Classes");

        await using (var owner = AsOwner(s))
        {
            // Chirag left the batch 5 days ago.
            var enrollment = await owner.Db.Enrollments.SingleAsync(e => e.StudentId == s.Students[2]);
            (await owner.Get<EnrollmentService>().DropAsync(enrollment.Id, s.Today.AddDays(-5))).IsSuccess.ShouldBeTrue();

            var otherPlan = await owner.Get<FeePlanService>().CreateAsync(new FeePlanRequest("One-time", BillingType.OneTime, 5000, null, null, null, 5));
            var other = await owner.Get<BatchService>().CreateAsync(new BatchRequest(
                "Other batch", null, null, null, null, EveryDay, null, s.Today, null, otherPlan.Value));

            var service = owner.Get<AttendanceService>();
            (await service.GetSheetAsync(s.BatchId, s.Today))!.Rows.Count.ShouldBe(2);
            (await service.GetSheetAsync(s.BatchId, s.Today.AddDays(-10)))!.Rows.Count.ShouldBe(3);
            (await service.GetSheetAsync(s.BatchId, s.Today.AddDays(-40)))!.Rows.Count.ShouldBe(0);   // before anyone joined

            await using var teacher = AsTeacher(s);
            (await teacher.Get<AttendanceService>().GetSheetAsync(other.Value, s.Today)).ShouldBeNull();
            (await teacher.Get<AttendanceService>().GetTodayAsync()).Select(b => b.Name).ShouldBe(new[] { "Class 11 Maths" });
        }
    }
}
