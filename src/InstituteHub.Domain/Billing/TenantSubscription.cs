using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Billing;

public enum SubscriptionStatus { Trial, Active, PastDue, Cancelled }
public enum BillingCycle { Monthly, Yearly }

public class TenantSubscription : TenantEntity
{
    public Guid PlanId { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trial;
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
    public DateOnly CurrentPeriodStart { get; set; }
    public DateOnly CurrentPeriodEnd { get; set; }
    /// <summary>Razorpay subscription id (Phase 3).</summary>
    public string? ExternalSubscriptionId { get; set; }

    public SubscriptionPlan? Plan { get; set; }
}
