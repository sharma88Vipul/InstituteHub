using InstituteHub.Application.Fees;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Fees;

namespace InstituteHub.UnitTests.Application;

/// <summary>Design doc 6.3 – week 5 "done when": dues are generated correctly for all three billing types.</summary>
public class FeeDueGeneratorTests
{
    private static Enrollment Enrolled(int year, int month, int day, decimal discount = 0, DateOnly? ended = null) => new()
    {
        StudentId = Guid.CreateVersion7(),
        BatchId = Guid.CreateVersion7(),
        EnrolledOn = new DateOnly(year, month, day),
        EndedOn = ended,
        DiscountAmount = discount,
    };

    private static FeePlan Plan(BillingType type, decimal total = 0, decimal? monthly = null,
        short? count = null, short? interval = 1, short dueDay = 5) => new()
    {
        Name = "Plan", BillingType = type, TotalAmount = total, MonthlyAmount = monthly,
        InstalmentCount = count, InstalmentIntervalMonths = interval, DueDayOfMonth = dueDay,
    };

    // ------------------------------------------------------------- one-time

    [Fact]
    public void One_time_is_a_single_due_on_the_enrollment_date()
    {
        var en = Enrolled(2026, 10, 12, discount: 1000);

        var due = FeeDueGenerator.ForNewEnrollment(en, Plan(BillingType.OneTime, total: 12000), new DateOnly(2026, 10, 12)).Single();

        due.Title.ShouldBe("Course fee");
        due.DueDate.ShouldBe(new DateOnly(2026, 10, 12));
        due.Amount.ShouldBe(12000);
        due.DiscountAmount.ShouldBe(1000);
        due.Balance.ShouldBe(11000);
        due.EnrollmentId.ShouldBe(en.Id);
        due.StudentId.ShouldBe(en.StudentId);
        due.PeriodKey.ShouldBeNull();
        due.Status.ShouldBe(FeeDueStatus.Pending);
    }

    // ------------------------------------------------------------- instalments

    [Fact]
    public void Instalments_split_in_whole_rupees_and_last_takes_the_remainder()
    {
        var dues = FeeDueGenerator.Instalments(Enrolled(2026, 10, 1), Plan(BillingType.Instalments, total: 10000, count: 3));

        dues.Select(d => d.Amount).ShouldBe(new[] { 3333m, 3333m, 3334m });
        dues.Sum(d => d.Amount).ShouldBe(10000);
        dues.Select(d => d.Title).ShouldBe(new[] { "Instalment 1 of 3", "Instalment 2 of 3", "Instalment 3 of 3" });
    }

    [Fact]
    public void First_instalment_is_not_before_the_enrollment_date_then_due_day_every_interval()
    {
        // Joined on the 12th; due day is the 5th, so the first instalment is due on joining.
        var monthly = FeeDueGenerator.Instalments(Enrolled(2026, 10, 12), Plan(BillingType.Instalments, total: 40000, count: 4));
        monthly.Select(d => d.DueDate).ShouldBe(new[]
        {
            new DateOnly(2026, 10, 12), new DateOnly(2026, 11, 5), new DateOnly(2026, 12, 5), new DateOnly(2027, 1, 5),
        });

        // Joined on the 2nd; due day the 10th, every 3 months.
        var quarterly = FeeDueGenerator.Instalments(Enrolled(2026, 10, 2), Plan(BillingType.Instalments, total: 30000, count: 3, interval: 3, dueDay: 10));
        quarterly.Select(d => d.DueDate).ShouldBe(new[]
        {
            new DateOnly(2026, 10, 10), new DateOnly(2027, 1, 10), new DateOnly(2027, 4, 10),
        });
    }

    [Fact]
    public void Discount_comes_off_the_last_instalments_first()
    {
        var dues = FeeDueGenerator.Instalments(Enrolled(2026, 10, 1, discount: 4000), Plan(BillingType.Instalments, total: 10000, count: 3));

        dues.Select(d => d.DiscountAmount).ShouldBe(new[] { 0m, 666m, 3334m });
        dues.Sum(d => d.Balance).ShouldBe(6000);
        dues.ShouldAllBe(d => d.Balance >= 0);
    }

    [Fact]
    public void Discount_larger_than_the_fee_is_capped()
    {
        var dues = FeeDueGenerator.Instalments(Enrolled(2026, 10, 1, discount: 99999), Plan(BillingType.Instalments, total: 5000, count: 2));
        dues.Sum(d => d.Balance).ShouldBe(0);
    }

    // ------------------------------------------------------------- monthly

    [Fact]
    public void Monthly_creates_the_first_month_on_joining()
    {
        var dues = FeeDueGenerator.ForNewEnrollment(Enrolled(2026, 10, 20, discount: 500),
            Plan(BillingType.Monthly, monthly: 2500), today: new DateOnly(2026, 10, 20));

        var due = dues.Single();
        due.PeriodKey.ShouldBe("2026-10");
        due.Title.ShouldBe("October 2026");
        due.DueDate.ShouldBe(new DateOnly(2026, 10, 20));   // due day (5th) already passed in the joining month
        due.Amount.ShouldBe(2500);
        due.DiscountAmount.ShouldBe(500);                     // monthly discount applies to every month
    }

    [Fact]
    public void Monthly_catches_up_and_skips_existing_months()
    {
        var en = Enrolled(2026, 8, 20);
        var plan = Plan(BillingType.Monthly, monthly: 2000, dueDay: 7);

        var all = FeeDueGenerator.Monthly(en, plan, new DateOnly(2026, 10, 3), new HashSet<string>());
        all.Select(d => d.PeriodKey).ShouldBe(new[] { "2026-08", "2026-09", "2026-10" });
        all.Select(d => d.DueDate).ShouldBe(new[] { new DateOnly(2026, 8, 20), new DateOnly(2026, 9, 7), new DateOnly(2026, 10, 7) });

        var missing = FeeDueGenerator.Monthly(en, plan, new DateOnly(2026, 10, 3), new HashSet<string> { "2026-08", "2026-09" });
        missing.Select(d => d.PeriodKey).ShouldBe(new[] { "2026-10" });
    }

    [Fact]
    public void Monthly_stops_at_the_month_the_student_left()
    {
        var en = Enrolled(2026, 6, 1, ended: new DateOnly(2026, 8, 15));
        var dues = FeeDueGenerator.Monthly(en, Plan(BillingType.Monthly, monthly: 1500), new DateOnly(2026, 12, 1), new HashSet<string>());
        dues.Select(d => d.PeriodKey).ShouldBe(new[] { "2026-06", "2026-07", "2026-08" });
    }
}
