using FluentValidation;
using InstituteHub.Application.Common;
using InstituteHub.Domain.Fees;

namespace InstituteHub.Application.Fees;

public sealed record FeePlanRequest(
    string Name,
    BillingType BillingType,
    decimal TotalAmount,
    decimal? MonthlyAmount,
    short? InstalmentCount,
    short? InstalmentIntervalMonths,
    short DueDayOfMonth);

public sealed record FeePlanItem(
    Guid Id,
    string Name,
    BillingType BillingType,
    decimal TotalAmount,
    decimal? MonthlyAmount,
    short? InstalmentCount,
    short? InstalmentIntervalMonths,
    short DueDayOfMonth,
    bool IsActive)
{
    public string Summary => FeePlanSummary.Describe(BillingType, TotalAmount, MonthlyAmount, InstalmentCount);
}

public sealed record FeePlanOption(Guid Id, string Name, string Summary);

public static class FeePlanSummary
{
    public static string Describe(BillingType type, decimal total, decimal? monthly, short? instalments) => type switch
    {
        BillingType.OneTime => $"{Money.Format(total)} one-time",
        BillingType.Instalments => $"{Money.Format(total)} in {instalments ?? 1} instalments",
        BillingType.Monthly => $"{Money.Format(monthly ?? 0)} per month",
        _ => Money.Format(total),
    };
}

public sealed class FeePlanRequestValidator : AbstractValidator<FeePlanRequest>
{
    public FeePlanRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter a name for the fee plan.").MaximumLength(100);
        RuleFor(x => x.DueDayOfMonth).InclusiveBetween((short)1, (short)28)
            .WithMessage("The due day must be between 1 and 28.");

        When(x => x.BillingType is BillingType.OneTime or BillingType.Instalments, () =>
            RuleFor(x => x.TotalAmount).GreaterThan(0).WithMessage("Enter the total fee."));

        When(x => x.BillingType == BillingType.Instalments, () =>
        {
            RuleFor(x => x.InstalmentCount).NotNull().InclusiveBetween((short)2, (short)24)
                .WithMessage("Instalments must be between 2 and 24.");
            RuleFor(x => x.InstalmentIntervalMonths).InclusiveBetween((short)1, (short)12)
                .When(x => x.InstalmentIntervalMonths is not null)
                .WithMessage("The gap between instalments must be 1 to 12 months.");
        });

        When(x => x.BillingType == BillingType.Monthly, () =>
            RuleFor(x => x.MonthlyAmount).NotNull().GreaterThan(0).WithMessage("Enter the monthly fee."));
    }
}
