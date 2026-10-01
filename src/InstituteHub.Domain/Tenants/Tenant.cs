using InstituteHub.Domain.Common;

namespace InstituteHub.Domain.Tenants;

/// <summary>One row per institute. Not filtered by tenant; only platform admins can list all.</summary>
public class Tenant : BaseEntity, IAuditable
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Email { get; set; }
    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? Gstin { get; set; }
    public string? LogoPath { get; set; }
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public string ReceiptPrefix { get; set; } = "";
    public TenantStatus Status { get; set; } = TenantStatus.Trial;
    public DateTimeOffset? TrialEndsAt { get; set; }
}
