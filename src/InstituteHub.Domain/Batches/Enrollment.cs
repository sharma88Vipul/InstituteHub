using InstituteHub.Domain.Common;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Students;

namespace InstituteHub.Domain.Batches;

public enum EnrollmentStatus { Active, Completed, Dropped }

public class Enrollment : TenantEntity, IAuditable
{
    public Guid StudentId { get; set; }
    public Guid BatchId { get; set; }
    public Guid FeePlanId { get; set; }
    public DateOnly EnrolledOn { get; set; }
    public DateOnly? EndedOn { get; set; }
    /// <summary>Total discount, spread over the generated dues.</summary>
    public decimal DiscountAmount { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;

    public Student? Student { get; set; }
    public Batch? Batch { get; set; }
    public FeePlan? FeePlan { get; set; }
    public ICollection<FeeDue> FeeDues { get; } = [];
}
