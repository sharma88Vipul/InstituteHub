using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Messaging;
using Microsoft.Extensions.Logging;

namespace InstituteHub.Infrastructure.Messaging;

/// <summary>
/// Development sender (design doc 10): writes the message to the log instead of sending it and returns a
/// "fake-…" provider id, so the message log, receipts and delivery updates can be tried without a provider account.
/// </summary>
public sealed class FakeMessageSender(ILogger<FakeMessageSender> logger) : IMessageSender
{
    public const string IdPrefix = "fake-";

    public Task<SendResult> SendAsync(OutgoingMessage message, CancellationToken ct)
    {
        var id = IdPrefix + Guid.CreateVersion7().ToString("N");
        logger.LogInformation(
            "FAKE {Channel} {TemplateCode} to {Phone} ({ProviderMessageId}): {Body}",
            message.Channel, message.TemplateCode, MessageFormat.MaskPhone(message.ToPhone), id, message.RenderedBody);
        return Task.FromResult(SendResult.Ok(id));
    }
}
