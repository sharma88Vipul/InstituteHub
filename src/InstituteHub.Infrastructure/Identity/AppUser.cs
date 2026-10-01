using Microsoft.AspNetCore.Identity;

namespace InstituteHub.Infrastructure.Identity;

/// <summary>Identity user (table asp_net_users) with the institute it belongs to.</summary>
public class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    /// <summary>Null only for PlatformAdmin users.</summary>
    public Guid? TenantId { get; set; }
    public string FullName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
}
