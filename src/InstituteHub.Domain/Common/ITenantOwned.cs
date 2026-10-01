namespace InstituteHub.Domain.Common;

/// <summary>Marks a row that belongs to exactly one institute (tenant).</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
