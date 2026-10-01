using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Fees;

public class PaymentAllocation : TenantEntity
{
    public Guid PaymentId { get; set; }
    public Guid FeeDueId { get; set; }
    public decimal Amount { get; set; }

    public FeeDue? FeeDue { get; set; }
}
