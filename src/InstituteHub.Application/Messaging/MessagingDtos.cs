using InstituteHub.Domain.Messaging;

namespace InstituteHub.Application.Messaging;

/// <summary>Related entity names stored in message_logs.related_entity.</summary>
public static class MessageRelated
{
    public const string Payment = "Payment";
    public const string FeeDue = "FeeDue";
    public const string AttendanceSession = "AttendanceSession";
}

public sealed record MessageLogQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    string? TemplateCode = null,
    MessageStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record MessageLogItem(
    Guid Id,
    DateTimeOffset CreatedAt,
    string TemplateCode,
    MessageChannel Channel,
    string ToPhone,
    string? GuardianName,
    Guid? StudentId,
    string? StudentName,
    MessageStatus Status,
    string? Error,
    string? ProviderMessageId);

public sealed record MessageStatusCounts(int Sent, int Delivered, int Read, int Failed);

public sealed record MessageLogPage(
    IReadOnlyList<MessageLogItem> Items,
    int TotalCount,
    MessageStatusCounts Counts,
    int Page,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

/// <summary>Outcome of one run of a messaging job for one institute.</summary>
public sealed record MessagingRunResult(int Sent, int Failed, int Skipped)
{
    public static readonly MessagingRunResult Empty = new(0, 0, 0);
    public override string ToString() => $"sent {Sent}, failed {Failed}, skipped {Skipped}";
}

/// <summary>What the institute's plan allows (subscription_plans.features). Trials get everything.</summary>
public sealed record MessagingFeatures(bool Reminders, bool Sms, bool AbsentAlerts);
