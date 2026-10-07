using InstituteHub.Application.Attendance;
using InstituteHub.Domain.Attendance;

namespace InstituteHub.UnitTests.Application;

public class AttendanceSummaryTests
{
    [Fact]
    public void Late_counts_as_present_and_leave_is_excluded()
    {
        var summary = AttendanceSummary.From(
        [
            AttendanceStatus.Present, AttendanceStatus.Present, AttendanceStatus.Late,
            AttendanceStatus.Absent, AttendanceStatus.Leave,
        ]);

        summary.Total.ShouldBe(5);
        summary.Percentage.ShouldBe(75.0);   // (2 present + 1 late) / (5 - 1 leave)
    }

    [Fact]
    public void Only_leave_has_no_percentage() =>
        AttendanceSummary.From([AttendanceStatus.Leave]).Percentage.ShouldBeNull();

    [Fact]
    public void Empty_has_no_percentage() => AttendanceSummary.Empty.Percentage.ShouldBeNull();
}
