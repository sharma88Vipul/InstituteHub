using System.Globalization;

namespace InstituteHub.Application.Common;

public static class Money
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>₹12,34,567 (Indian digit grouping, no paise when whole rupees).</summary>
    public static string Format(decimal amount) =>
        "₹" + amount.ToString(amount == decimal.Truncate(amount) ? "N0" : "N2", India);
}
