using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Messaging;

public enum MessageStatus { Queued, Sent, Delivered, Read, Failed }

public class MessageLog : TenantEntity
{
    public Guid? GuardianId { get; set; }
    public Guid? StudentId { get; set; }
    public string TemplateCode { get; set; } = "";
    public MessageChannel Channel { get; set; }
    public string ToPhone { get; set; } = "";
    /// <summary>Placeholder values sent, stored as jsonb.</summary>
    public string Payload { get; set; } = "{}";
    public string? RelatedEntity { get; set; }
    public Guid? RelatedId { get; set; }
    public MessageStatus Status { get; set; } = MessageStatus.Queued;
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
