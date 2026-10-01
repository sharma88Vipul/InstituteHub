using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Batches;
using InstituteHub.Application.Common;
using InstituteHub.Application.Fees;
using InstituteHub.Application.Students;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Students;

namespace InstituteHub.UnitTests.Application;

file sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow => new(2026, 10, 1, 6, 0, 0, TimeSpan.Zero);
    public DateOnly Today(string timeZoneId = "Asia/Kolkata") => new(2026, 10, 1);
}

public class AdmissionRequestValidatorTests
{
    private readonly AdmissionRequestValidator _validator = new(new FixedClock());

    private static AdmissionRequest Valid() => new(
        null, "Priya", null, null, null, null, null, null, null, new DateOnly(2026, 10, 1), null,
        null, new GuardianInput("Rakesh", GuardianRelation.Father, "9876543210", null, null, true),
        null, null, 0);

    [Fact]
    public void Valid_admission_passes() => _validator.Validate(Valid()).IsValid.ShouldBeTrue();

    [Fact]
    public void Guardian_is_required() =>
        _validator.Validate(Valid() with { NewGuardian = null }).IsValid.ShouldBeFalse();

    [Fact]
    public void Existing_guardian_is_enough() =>
        _validator.Validate(Valid() with { NewGuardian = null, ExistingGuardianId = Guid.NewGuid() }).IsValid.ShouldBeTrue();

    [Fact]
    public void Guardian_phone_must_be_valid() =>
        _validator.Validate(Valid() with { NewGuardian = Valid().NewGuardian! with { Phone = "123" } }).IsValid.ShouldBeFalse();

    [Fact]
    public void Date_of_birth_cannot_be_in_the_future() =>
        _validator.Validate(Valid() with { DateOfBirth = new DateOnly(2030, 1, 1) }).IsValid.ShouldBeFalse();
}

public class BatchAndFeePlanValidatorTests
{
    [Fact]
    public void Batch_needs_days_and_end_after_start()
    {
        var validator = new BatchRequestValidator();
        var request = new BatchRequest("Physics", null, null, new TimeOnly(18, 0), new TimeOnly(17, 0),
            WeekDays.None, null, new DateOnly(2026, 10, 1), null, null);

        var result = validator.Validate(request);

        result.Errors.Select(e => e.PropertyName).ShouldBe(new[] { "DaysOfWeek", "EndTime" }, ignoreOrder: true);
    }

    [Theory]
    [InlineData(BillingType.OneTime, 0, null, null, false)]
    [InlineData(BillingType.OneTime, 12000, null, null, true)]
    [InlineData(BillingType.Instalments, 40000, null, (short)1, false)]
    [InlineData(BillingType.Instalments, 40000, null, (short)4, true)]
    [InlineData(BillingType.Monthly, 0, null, null, false)]
    [InlineData(BillingType.Monthly, 0, 2500, null, true)]
    public void Fee_plan_rules(BillingType type, int total, int? monthly, short? instalments, bool valid)
    {
        var request = new FeePlanRequest("Plan", type, total, monthly, instalments, 1, 5);
        new FeePlanRequestValidator().Validate(request).IsValid.ShouldBe(valid);
    }
}

public class FormattingTests
{
    [Theory]
    [InlineData(WeekDays.Monday | WeekDays.Wednesday | WeekDays.Friday, 17, 18, "Mon, Wed, Fri · 17:00–18:00")]
    [InlineData((WeekDays)63, null, null, "Mon–Sat")]
    public void Batch_schedule(WeekDays days, int? start, int? end, string expected) =>
        BatchSchedule.Describe(days,
            start is { } s ? new TimeOnly(s, 0) : null,
            end is { } e ? new TimeOnly(e, 0) : null).ShouldBe(expected);

    [Fact]
    public void Money_uses_indian_grouping() => Money.Format(1234567m).ShouldBe("₹12,34,567");

    [Fact]
    public void Fee_plan_summary() =>
        FeePlanSummary.Describe(BillingType.Instalments, 40000, null, 4).ShouldBe("₹40,000 in 4 instalments");

    [Theory]
    [InlineData("+919876543210", "+91 98765 43210")]
    [InlineData(null, "")]
    [InlineData("+442079460958", "+442079460958")]
    public void Phone_display(string? input, string expected) => PhoneNumber.Format(input).ShouldBe(expected);
}
