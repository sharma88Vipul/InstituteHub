using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Messaging;

public enum MessageChannel { WhatsApp, Sms }

public static class MessageTemplateCodes
{
    public const string FeeReminder = "FEE_REMINDER";
    public const string FeeOverdue = "FEE_OVERDUE";
    public const string PaymentReceipt = "PAYMENT_RECEIPT";
    public const string AbsentAlert = "ABSENT_ALERT";
}

/// <summary>TenantId null = system default template.</summary>
public class MessageTemplate : BaseEntity
{
    public Guid? TenantId { get; set; }
    public string Code { get; set; } = "";
    public MessageChannel Channel { get; set; }
    /// <summary>Approved WhatsApp template name or DLT template id for SMS.</summary>
    public string? ProviderTemplateId { get; set; }
    /// <summary>Body with placeholders such as {{student}}, {{amount}}, {{due_date}}.</summary>
    public string Body { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
