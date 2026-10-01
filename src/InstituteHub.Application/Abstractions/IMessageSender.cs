namespace InstituteHub.Application.Abstractions;

public sealed record OutgoingMessage(
    string ToPhone,
    string TemplateCode,
    string? ProviderTemplateId,
    string RenderedBody,
    IReadOnlyDictionary<string, string> Placeholders);

public sealed record SendResult(bool Success, string? ProviderMessageId, string? Error);

/// <summary>WhatsApp/SMS gateway. A fake implementation logs messages in development (week 7).</summary>
public interface IMessageSender
{
    Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct);
}
