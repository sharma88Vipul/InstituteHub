namespace InstituteHub.Application.Abstractions;

/// <summary>
/// Public receipt links sent to parents (design doc 5.4: GET /r/{token}). The token is signed and expires,
/// so a link cannot be guessed or altered to open another receipt.
/// </summary>
public interface IReceiptLinks
{
    /// <summary>Absolute URL of the receipt PDF for a parent, valid for <see cref="Lifetime"/>.</summary>
    string Create(Guid tenantId, Guid paymentId);

    /// <summary>The tenant and payment of a valid, unexpired token; null otherwise.</summary>
    (Guid TenantId, Guid PaymentId)? Read(string token);

    TimeSpan Lifetime { get; }
}
