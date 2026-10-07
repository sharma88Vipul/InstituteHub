using System.Globalization;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Fees;

namespace InstituteHub.Application.Fees;

/// <summary>
/// Turns an enrollment + fee plan into fee dues (design doc 6.3). Pure functions: no database, easy to test.
/// <list type="bullet">
/// <item>OneTime – one due for the full amount, due on the enrollment date.</item>
/// <item>Instalments – N dues of floor(total ÷ N) rupees, the last absorbs the remainder. The first is due in the
/// enrollment month on the plan's due day (not before the enrollment date), then every interval months.
/// The enrollment discount is taken off the last instalments first.</item>
/// <item>Monthly – one due per month with period_key yyyy-MM, from the enrollment month up to a given month.
/// The enrollment discount applies to every month.</item>
/// </list>
/// Amount is always the gross amount; the discount is stored separately in DiscountAmount.
/// </summary>
public static class FeeDueGenerator
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>All dues to create when a student joins. For monthly plans this covers months up to <paramref name="today"/>.</summary>
    public static IReadOnlyList<FeeDue> ForNewEnrollment(Enrollment enrollment, FeePlan plan, DateOnly today) => plan.BillingType switch
    {
        BillingType.OneTime => [OneTime(enrollment, plan)],
        BillingType.Instalments => Instalments(enrollment, plan),
        BillingType.Monthly => Monthly(enrollment, plan, Max(today, enrollment.EnrolledOn), new HashSet<string>()),
        _ => [],
    };

    public static FeeDue OneTime(Enrollment enrollment, FeePlan plan) =>
        NewDue(enrollment, "Course fee", null, enrollment.EnrolledOn, plan.TotalAmount,
            Math.Min(enrollment.DiscountAmount, plan.TotalAmount));

    public static IReadOnlyList<FeeDue> Instalments(Enrollment enrollment, FeePlan plan)
    {
        var count = Math.Max(1, (int)(plan.InstalmentCount ?? 1));
        var interval = Math.Max(1, (int)(plan.InstalmentIntervalMonths ?? 1));
        var total = plan.TotalAmount;

        // Whole rupees per instalment; the last one absorbs the remainder.
        var each = Math.Floor(total / count);
        var amounts = Enumerable.Range(0, count)
            .Select(i => i == count - 1 ? total - each * (count - 1) : each)
            .ToArray();

        // Discount comes off the last instalments first.
        var discounts = new decimal[count];
        var remaining = Math.Min(enrollment.DiscountAmount, total);
        for (var i = count - 1; i >= 0 && remaining > 0; i--)
        {
            discounts[i] = Math.Min(remaining, amounts[i]);
            remaining -= discounts[i];
        }

        var first = DueDateInMonth(enrollment.EnrolledOn, plan.DueDayOfMonth, enrollment.EnrolledOn);
        var dues = new List<FeeDue>(count);
        for (var i = 0; i < count; i++)
        {
            var dueDate = i == 0 ? first : FirstOfMonth(enrollment.EnrolledOn).AddMonths(i * interval).AddDays(plan.DueDayOfMonth - 1);
            dues.Add(NewDue(enrollment, $"Instalment {i + 1} of {count}", null, dueDate, amounts[i], discounts[i]));
        }
        return dues;
    }

    /// <summary>
    /// Monthly dues from the enrollment month through <paramref name="throughDate"/>'s month (or the month the
    /// enrollment ended, if earlier). Months whose period key already exists are skipped, so it is safe to run
    /// again (the monthly job relies on this).
    /// </summary>
    public static IReadOnlyList<FeeDue> Monthly(Enrollment enrollment, FeePlan plan, DateOnly throughDate, IReadOnlySet<string> existingPeriodKeys)
    {
        var monthly = plan.MonthlyAmount ?? 0;
        if (monthly <= 0) return [];

        var last = FirstOfMonth(throughDate);
        if (enrollment.EndedOn is { } ended && FirstOfMonth(ended) < last) last = FirstOfMonth(ended);

        var discount = Math.Min(enrollment.DiscountAmount, monthly);
        var dues = new List<FeeDue>();
        for (var month = FirstOfMonth(enrollment.EnrolledOn); month <= last; month = month.AddMonths(1))
        {
            var key = PeriodKey(month);
            if (existingPeriodKeys.Contains(key)) continue;

            var dueDate = DueDateInMonth(month, plan.DueDayOfMonth, enrollment.EnrolledOn);
            dues.Add(NewDue(enrollment, month.ToString("MMMM yyyy", India), key, dueDate, monthly, discount));
        }
        return dues;
    }

    public static string PeriodKey(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>The plan's due day in <paramref name="month"/>, but never before the enrollment date.</summary>
    private static DateOnly DueDateInMonth(DateOnly month, short dueDay, DateOnly enrolledOn)
    {
        var date = new DateOnly(month.Year, month.Month, Math.Clamp((int)dueDay, 1, 28));
        return date < enrolledOn ? enrolledOn : date;
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static FeeDue NewDue(Enrollment enrollment, string title, string? periodKey, DateOnly dueDate, decimal amount, decimal discount) => new()
    {
        TenantId = enrollment.TenantId,
        EnrollmentId = enrollment.Id,
        StudentId = enrollment.StudentId,
        Title = title,
        PeriodKey = periodKey,
        DueDate = dueDate,
        Amount = amount,
        DiscountAmount = discount,
    };
}
