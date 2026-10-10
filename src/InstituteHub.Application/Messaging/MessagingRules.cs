using System.Globalization;
using System.Text.RegularExpressions;
using InstituteHub.Domain.Messaging;

namespace InstituteHub.Application.Messaging;

/// <summary>Fills {{placeholders}} in a message template body.</summary>
public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{\{\s*([a-z_]+)\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex Placeholder();

    /// <summary>Replaces each {{name}} with its value; unknown placeholders become empty text.</summary>
    public static string Render(string body, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(body, m =>
            values.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var value) ? value : "");
}

public enum ReminderKind { None, Upcoming, DueToday, Overdue }

/// <summary>
/// Which unpaid dues get a reminder today (design doc 6.6): due in <see cref="DaysBefore"/> days, due today,
/// or overdue and not reminded in the last <see cref="OverdueRepeatDays"/> days. Never twice on the same day,
/// so the job can be retried or run by hand safely.
/// </summary>
public static class ReminderRules
{
    public const int DaysBefore = 3;
    public const int OverdueRepeatDays = 7;

    public static ReminderKind Classify(DateOnly dueDate, DateOnly today, DateOnly? lastRemindedOn)
    {
        if (lastRemindedOn is { } last && last >= today) return ReminderKind.None;

        if (dueDate == today.AddDays(DaysBefore)) return ReminderKind.Upcoming;
        if (dueDate == today) return ReminderKind.DueToday;
        if (dueDate < today && (lastRemindedOn is null || today.DayNumber - lastRemindedOn.Value.DayNumber >= OverdueRepeatDays))
            return ReminderKind.Overdue;

        return ReminderKind.None;
    }
}

/// <summary>Delivery updates can arrive late or out of order; a message never moves backwards.</summary>
public static class MessageStatusRules
{
    private static int Rank(MessageStatus s) => s switch
    {
        MessageStatus.Queued => 0,
        MessageStatus.Sent => 1,
        MessageStatus.Delivered => 2,
        MessageStatus.Read => 3,
        _ => -1,
    };

    public static bool CanMove(MessageStatus from, MessageStatus to)
    {
        if (from == to) return false;
        // Failed is final, and a message the parent already received cannot fail afterwards.
        if (from == MessageStatus.Failed) return false;
        if (to == MessageStatus.Failed) return Rank(from) < Rank(MessageStatus.Delivered);
        return Rank(to) > Rank(from);
    }

    /// <summary>Maps provider status words (sent, delivered, read, failed, undelivered…) to ours.</summary>
    public static MessageStatus? Parse(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "sent" or "submitted" or "enqueued" => MessageStatus.Sent,
        "delivered" => MessageStatus.Delivered,
        "read" or "seen" => MessageStatus.Read,
        "failed" or "undelivered" or "rejected" or "error" => MessageStatus.Failed,
        _ => null,
    };
}

/// <summary>How values look inside a message.</summary>
public static class MessageFormat
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>12,500 or 12,500.50 (Indian grouping; the template adds "Rs").</summary>
    public static string Amount(decimal amount) =>
        amount.ToString(amount == decimal.Truncate(amount) ? "N0" : "N2", India);

    public static string Date(DateOnly date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>"Aarav", "Aarav and Diya", "Aarav, Diya and Kabir".</summary>
    public static string Names(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    /// <summary>+91******3210 – for logs, which must not contain full phone numbers.</summary>
    public static string MaskPhone(string phone) =>
        phone.Length <= 4 ? "****" : (phone.StartsWith('+') && phone.Length > 7 ? phone[..3] : "") + new string('*', 6) + phone[^4..];
}
