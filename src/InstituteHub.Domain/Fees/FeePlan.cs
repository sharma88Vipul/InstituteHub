using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Fees;

public enum BillingType { OneTime, Instalments, Monthly }

public class FeePlan : TenantEntity, IAuditable
{
    public string Name { get; set; } = "";
    public BillingType BillingType { get; set; }
    /// <summary>For OneTime / Instalments.</summary>
    public decimal TotalAmount { get; set; }
    /// <summary>For Monthly.</summary>
    public decimal? MonthlyAmount { get; set; }
    public short? InstalmentCount { get; set; }
    public short? InstalmentIntervalMonths { get; set; } = 1;
    /// <summary>1–28.</summary>
    public short DueDayOfMonth { get; set; } = 5;
    public bool IsActive { get; set; } = true;
}
