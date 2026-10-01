using System.Security.Claims;
using InstituteHub.Application.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace InstituteHub.Infrastructure.Identity;

/// <summary>Adds the tenant_id claim to the auth cookie at sign-in.</summary>
public sealed class AppUserClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, AppRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.TenantId is { } tenantId)
        {
            identity.AddClaim(new Claim(AppClaimTypes.TenantId, tenantId.ToString()));
        }
        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            identity.AddClaim(new Claim("full_name", user.FullName));
        }
        return identity;
    }
}
