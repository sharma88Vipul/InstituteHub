using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Billing;

/// <summary>Platform-level plan (no tenant_id): STARTER, GROWTH, PRO.</summary>
public class SubscriptionPlan : BaseEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int MaxStudents { get; set; }
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    /// <summary>Feature flags as jsonb, e.g. {"reminders": true}.</summary>
    public string Features { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
}
