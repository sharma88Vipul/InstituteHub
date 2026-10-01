using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;

namespace InstituteHub.Infrastructure.Identity;

/// <summary>Job context first, then the tenant_id claim of the signed-in user.</summary>
public sealed class TenantProvider(JobTenantContext job, UserContextAccessor user) : ITenantProvider
{
    public Guid? CurrentTenantId =>
        job.TenantId
        ?? (Guid.TryParse(user.User?.FindFirst(AppClaimTypes.TenantId)?.Value, out var id) ? id : null);
}
