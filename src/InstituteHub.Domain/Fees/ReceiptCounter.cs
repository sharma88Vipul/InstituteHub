using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Fees;

/// <summary>Receipt number sequence per tenant and financial year. PK = (TenantId, FinancialYear).</summary>
public class ReceiptCounter : ITenantOwned
{
    public Guid TenantId { get; set; }
    /// <summary>e.g. 2026-27.</summary>
    public string FinancialYear { get; set; } = "";
    public int LastNumber { get; set; }

    /// <summary>Indian financial year (April–March) for a date, e.g. 2026-27.</summary>
    public static string FinancialYearFor(DateOnly date) => date.Month >= 4
        ? $"{date.Year}-{(date.Year + 1) % 100:00}"
        : $"{date.Year - 1}-{date.Year % 100:00}";
}
