using System.Security.Cryptography;
using InstituteHub.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace InstituteHub.Infrastructure.Messaging;

/// <summary>
/// Signed, expiring receipt links (design doc 5.4 / 7.1) built on ASP.NET Data Protection, whose keys are already
/// persisted for sign-in cookies. The token carries the institute and payment ids and cannot be altered.
/// </summary>
public sealed class ReceiptLinks(IDataProtectionProvider dataProtection, IOptions<AppUrlOptions> options) : IReceiptLinks
{
    private readonly ITimeLimitedDataProtector _protector =
        dataProtection.CreateProtector("InstituteHub.ReceiptLink.v1").ToTimeLimitedDataProtector();

    public TimeSpan Lifetime { get; } = TimeSpan.FromDays(90);

    public string Create(Guid tenantId, Guid paymentId)
    {
        var token = _protector.Protect($"{tenantId:N}.{paymentId:N}", Lifetime);
        var baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');
        return $"{baseUrl}/r/{token}";
    }

    public (Guid TenantId, Guid PaymentId)? Read(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1000) return null;
        try
        {
            var parts = _protector.Unprotect(token, out _).Split('.');
            return parts.Length == 2 && Guid.TryParse(parts[0], out var tenantId) && Guid.TryParse(parts[1], out var paymentId)
                ? (tenantId, paymentId)
                : null;
        }
        catch (CryptographicException)
        {
            return null; // tampered, expired or from another key ring
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
