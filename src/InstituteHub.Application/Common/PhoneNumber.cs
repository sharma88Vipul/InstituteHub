using System.Text.RegularExpressions;

namespace InstituteHub.Application.Common;

/// <summary>Phone numbers are stored in E.164 format, e.g. +919876543210 (design doc 4.1).</summary>
public static partial class PhoneNumber
{
    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164();

    /// <summary>
    /// Normalises what people type ("98765 43210", "098765-43210", "+91 98765 43210") to E.164.
    /// Numbers without a country code are treated as Indian mobile numbers. Returns null when invalid.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        string candidate;

        if (input.TrimStart().StartsWith('+'))
            candidate = "+" + digits;
        else if (digits.Length == 10 && digits[0] is >= '6' and <= '9')
            candidate = "+91" + digits;
        else if (digits.Length == 11 && digits[0] == '0')
            candidate = "+91" + digits[1..];
        else if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
            candidate = "+" + digits;
        else
            return null;

        return E164().IsMatch(candidate) ? candidate : null;
    }

    /// <summary>"+919876543210" → "+91 98765 43210" for display; other numbers are returned unchanged.</summary>
    public static string Format(string? e164)
    {
        if (string.IsNullOrWhiteSpace(e164)) return "";
        return e164.Length == 13 && e164.StartsWith("+91", StringComparison.Ordinal)
            ? $"+91 {e164[3..8]} {e164[8..]}"
            : e164;
    }

    /// <summary>Link that opens a WhatsApp chat with this number.</summary>
    public static string WhatsAppLink(string e164) => "https://wa.me/" + e164.TrimStart('+');
}
