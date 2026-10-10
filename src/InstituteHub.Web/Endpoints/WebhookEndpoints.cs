using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InstituteHub.Application.Messaging;
using InstituteHub.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace InstituteHub.Web.Endpoints;

/// <summary>
/// POST /webhooks/whatsapp – delivery updates from the WhatsApp/SMS provider (design doc 5.4 / 6.6).
/// Body: one update or an array, e.g. {"messageId":"…","status":"delivered","error":null}.
/// Status words: sent, delivered, read, failed (also undelivered/rejected). When a real provider is connected its own
/// payload is mapped to this shape here.
/// Security: header X-Signature = "sha256=" + hex(HMAC-SHA256(body, Messaging:WebhookSecret)). Without a secret the
/// endpoint only works in Development.
/// </summary>
public static class WebhookEndpoints
{
    public const string SignatureHeader = "X-Signature";
    private const int MaxBodyLength = 256 * 1024;

    private sealed record StatusUpdate(string? MessageId, string? Status, string? Error);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/whatsapp", HandleAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .WithName("WhatsAppWebhook");
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request, MessagingService messaging, IOptions<MessagingOptions> options,
        IHostEnvironment environment, ILoggerFactory loggers, CancellationToken ct)
    {
        var logger = loggers.CreateLogger("InstituteHub.Web.Webhooks");
        if (request.ContentLength > MaxBodyLength) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);

        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrEmpty(secret))
        {
            if (!environment.IsDevelopment())
            {
                logger.LogError("Messaging:WebhookSecret is not configured; delivery update rejected");
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }
        else if (!SignatureIsValid(body, request.Headers[SignatureHeader], secret))
        {
            logger.LogWarning("Delivery update with a missing or wrong signature rejected");
            return Results.Unauthorized();
        }

        List<StatusUpdate> updates;
        try
        {
            updates = body.TrimStart().StartsWith('[')
                ? JsonSerializer.Deserialize<List<StatusUpdate>>(body, Json) ?? []
                : JsonSerializer.Deserialize<StatusUpdate>(body, Json) is { } one ? [one] : [];
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Body must be JSON: {\"messageId\":\"…\",\"status\":\"delivered\"}" });
        }

        var applied = 0;
        foreach (var update in updates)
        {
            if (string.IsNullOrWhiteSpace(update.MessageId) || MessageStatusRules.Parse(update.Status) is not { } status) continue;
            if (await messaging.ApplyDeliveryStatusAsync(update.MessageId, status, update.Error, ct)) applied++;
        }

        logger.LogInformation("Delivery updates received {Received}, applied {Applied}", updates.Count, applied);
        return Results.Ok(new { received = updates.Count, applied });
    }

    /// <summary>The expected X-Signature value for a body (also handy for testing the webhook by hand).</summary>
    public static string Sign(string body, string secret) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    private static bool SignatureIsValid(string body, string? header, string secret)
    {
        if (string.IsNullOrWhiteSpace(header)) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(body, secret));
        var actual = Encoding.ASCII.GetBytes(header.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
