namespace InstituteHub.Application.Common;

/// <summary>Indian-style amount in words for receipts: 125000.50 → "Rupees One Lakh Twenty-Five Thousand and Fifty Paise Only".</summary>
public static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string Rupees(decimal amount)
    {
        if (amount < 0) amount = -amount;
        var rupees = (long)decimal.Truncate(amount);
        var paise = (int)Math.Round((amount - rupees) * 100, MidpointRounding.AwayFromZero);
        if (paise == 100) { rupees++; paise = 0; }

        var words = rupees == 0 ? "Zero" : Number(rupees);
        var result = "Rupees " + words;
        if (paise > 0) result += " and " + TwoDigits(paise) + " Paise";
        return result + " Only";
    }

    private static string Number(long n)
    {
        var parts = new List<string>();
        void Take(long divisor, string name)
        {
            if (n < divisor) return;
            parts.Add(Number(n / divisor) + " " + name);
            n %= divisor;
        }

        Take(10_000_000, "Crore");
        Take(100_000, "Lakh");
        Take(1_000, "Thousand");
        Take(100, "Hundred");
        if (n > 0) parts.Add(TwoDigits((int)n));
        return string.Join(" ", parts);
    }

    private static string TwoDigits(int n) => n < 20
        ? Ones[n]
        : Tens[n / 10] + (n % 10 > 0 ? "-" + Ones[n % 10] : "");
}
