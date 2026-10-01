namespace InstituteHub.Application.Abstractions;

/// <summary>
/// Resolves the institute (tenant) for the current request or background job.
/// </summary>
public interface ITenantProvider
{
    /// <summary>The current tenant id, or null when there is no tenant context (platform admin, seeding, design time).</summary>
    Guid? CurrentTenantId { get; }

    /// <summary>The current tenant id; throws when there is no tenant context.</summary>
    Guid TenantId => CurrentTenantId ?? throw new InvalidOperationException("No tenant in current context.");
}
