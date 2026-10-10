namespace InstituteHub.Infrastructure.Messaging;

/// <summary>"Messaging" section (design doc 10: Messaging__Provider, Messaging__ApiKey, Messaging__WebhookSecret).</summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>Fake (development: messages are written to the log) or a real provider once one is chosen.</summary>
    public string Provider { get; set; } = "Fake";

    public string? ApiKey { get; set; }

    /// <summary>
    /// Shared secret for POST /webhooks/whatsapp (HMAC-SHA256 of the body in the X-Signature header).
    /// Required outside Development; when empty in Development the signature is not checked.
    /// </summary>
    public string? WebhookSecret { get; set; }
}

/// <summary>"App" section: public address used in links sent to parents.</summary>
public sealed class AppUrlOptions
{
    public const string SectionName = "App";

    /// <summary>e.g. https://app.institutehub.in (Development: https://localhost:7227).</summary>
    public string PublicBaseUrl { get; set; } = "";
}
