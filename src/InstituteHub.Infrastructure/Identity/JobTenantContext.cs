namespace InstituteHub.Infrastructure.Identity;

/// <summary>
/// Background jobs run outside an HTTP request: each job receives the tenant id as a parameter
/// and sets it here (per DI scope) before touching the database.
/// </summary>
public sealed class JobTenantContext
{
    public Guid? TenantId { get; set; }
}
