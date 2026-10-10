using InstituteHub.Domain.Messaging;
using MudBlazor;

namespace InstituteHub.Web.Features.Messaging;

/// <summary>Plain-language names and colours for message types and statuses.</summary>
public static class MessageLabels
{
    public static readonly IReadOnlyList<string> TemplateCodes =
    [
        MessageTemplateCodes.FeeReminder,
        MessageTemplateCodes.FeeOverdue,
        MessageTemplateCodes.PaymentReceipt,
        MessageTemplateCodes.AbsentAlert,
    ];

    public static string Template(string code) => code switch
    {
        MessageTemplateCodes.FeeReminder => "Fee reminder",
        MessageTemplateCodes.FeeOverdue => "Overdue reminder",
        MessageTemplateCodes.PaymentReceipt => "Receipt",
        MessageTemplateCodes.AbsentAlert => "Absent alert",
        _ => code,
    };

    public static string Channel(MessageChannel channel) => channel == MessageChannel.Sms ? "SMS" : "WhatsApp";

    public static Color StatusColor(MessageStatus status) => status switch
    {
        MessageStatus.Sent => Color.Info,
        MessageStatus.Delivered => Color.Success,
        MessageStatus.Read => Color.Success,
        MessageStatus.Failed => Color.Error,
        _ => Color.Default,
    };

    /// <summary>Indian time, e.g. 08 Oct, 10:02 AM.</summary>
    public static string When(DateTimeOffset at) => at.ToOffset(TimeSpan.FromHours(5.5)).ToString("dd MMM, hh:mm tt");
}
