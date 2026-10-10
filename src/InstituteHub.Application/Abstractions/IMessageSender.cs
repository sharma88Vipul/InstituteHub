using InstituteHub.Domain.Messaging;

namespace InstituteHub.Application.Abstractions;

public sealed record OutgoingMessage(
    MessageChannel Channel,
    string ToPhone,
    string TemplateCode,
    string? ProviderTemplateId,
    string RenderedBody,
    IReadOnlyDictionary<string, string> Placeholders);

public sealed record SendResult(bool Success, string? ProviderMessageId, string? Error)
{
    public static SendResult Ok(string providerMessageId) => new(true, providerMessageId, null);
    public static SendResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// WhatsApp/SMS gateway (design doc 5.5 / 6.6). Development uses FakeMessageSender, which logs the
/// message instead of sending it. A real provider (Gupshup, Interakt, Twilio…) plugs in behind this interface.
/// </summary>
public interface IMessageSender
{
    Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct);
}
