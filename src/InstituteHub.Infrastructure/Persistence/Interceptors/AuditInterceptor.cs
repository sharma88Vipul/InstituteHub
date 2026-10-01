using System.Text.Json;
using System.Text.Json.Serialization;
using InstituteHub.Application.Abstractions;
using InstituteHub.Domain.Auditing;
using InstituteHub.Domain.Common;
using InstituteHub.Domain.Fees;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace InstituteHub.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Runs before every SaveChanges:
///  1. Tenant guard – stamps tenant_id on new rows and refuses writes to another tenant's rows.
///  2. Soft delete – turns Remove() on a BaseEntity into is_deleted = true.
///  3. Audit columns – created_at/by, updated_at/by.
///  4. Audit log – one audit_logs row per change to an <see cref="IAuditable"/> entity.
/// </summary>
public sealed class AuditInterceptor(ITenantProvider tenant, ICurrentUser currentUser, IClock clock)
    : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext context)
    {
        context.ChangeTracker.DetectChanges();

        var now = clock.UtcNow;
        var userId = currentUser.UserId;
        var currentTenantId = tenant.CurrentTenantId;
        var auditLogs = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AuditLog || entry.State is EntityState.Detached or EntityState.Unchanged) continue;

            GuardTenant(entry, currentTenantId);

            if (entry.Entity is BaseEntity entity)
            {
                if (entry.State == EntityState.Deleted)
                {
                    entry.State = EntityState.Modified;
                    entity.IsDeleted = true;
                }

                if (entry.State == EntityState.Added)
                {
                    entity.CreatedAt = now;
                    entity.CreatedBy ??= userId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entity.UpdatedAt = now;
                    entity.UpdatedBy = userId;
                    entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(BaseEntity.CreatedBy)).IsModified = false;
                }
            }

            if (entry.Entity is IAuditable && CreateAuditLog(entry, now, userId, currentTenantId) is { } log)
            {
                auditLogs.Add(log);
            }
        }

        if (auditLogs.Count > 0) context.Set<AuditLog>().AddRange(auditLogs);
    }

    private static void GuardTenant(EntityEntry entry, Guid? currentTenantId)
    {
        if (entry.Entity is not ITenantOwned owned) return;

        if (entry.State == EntityState.Added)
        {
            if (owned.TenantId == Guid.Empty)
            {
                owned.TenantId = currentTenantId
                    ?? throw new InvalidOperationException(
                        $"Cannot save a new {entry.Metadata.ClrType.Name} without a tenant context.");
            }
        }
        else
        {
            // Never allow a row to be moved to another tenant.
            var tenantProp = entry.Property(nameof(ITenantOwned.TenantId));
            if (tenantProp.IsModified && !Equals(tenantProp.OriginalValue, tenantProp.CurrentValue))
            {
                throw new InvalidOperationException("tenant_id cannot be changed.");
            }
        }

        // Inside a tenant context, only that tenant's rows may be written.
        // (No tenant context = platform admin, seeding or a cross-tenant job; rows must carry their own tenant id.)
        if (currentTenantId is { } current && owned.TenantId != current)
        {
            throw new InvalidOperationException(
                $"Cross-tenant write blocked for {entry.Metadata.ClrType.Name}.");
        }
    }

    private static AuditLog? CreateAuditLog(EntityEntry entry, DateTimeOffset now, Guid? userId, Guid? currentTenantId)
    {
        var changes = new Dictionary<string, object?>();
        AuditAction action;

        switch (entry.State)
        {
            case EntityState.Added:
                action = AuditAction.Create;
                foreach (var p in entry.Properties.Where(p => !IsNoise(p)))
                    changes[p.Metadata.Name] = p.CurrentValue;
                break;

            case EntityState.Modified:
                var modified = entry.Properties.Where(p => p.IsModified && !IsNoise(p)
                                                           && !Equals(p.OriginalValue, p.CurrentValue)).ToList();
                if (modified.Count == 0) return null;

                action = modified.Any(p => p.Metadata.Name == nameof(BaseEntity.IsDeleted) && p.CurrentValue is true)
                    ? AuditAction.Delete
                    : entry.Entity is Payment && modified.Any(p => p.Metadata.Name == nameof(Payment.IsCancelled) && p.CurrentValue is true)
                        ? AuditAction.Cancel
                        : AuditAction.Update;

                foreach (var p in modified)
                    changes[p.Metadata.Name] = new { old = p.OriginalValue, @new = p.CurrentValue };
                break;

            default:
                return null;
        }

        var entityId = entry.Property("Id").CurrentValue is Guid id ? id : Guid.Empty;
        var tenantId = entry.Entity switch
        {
            ITenantOwned owned => owned.TenantId,
            Domain.Tenants.Tenant t => t.Id,
            _ => currentTenantId,
        };

        return new AuditLog
        {
            TenantId = tenantId,
            UserId = userId,
            EntityName = entry.Metadata.ClrType.Name,
            EntityId = entityId,
            Action = action,
            Changes = JsonSerializer.Serialize(changes, JsonOptions),
            CreatedAt = now,
        };
    }

    private static bool IsNoise(PropertyEntry p) => p.Metadata.Name is
        nameof(BaseEntity.CreatedAt) or nameof(BaseEntity.CreatedBy) or
        nameof(BaseEntity.UpdatedAt) or nameof(BaseEntity.UpdatedBy) or "xmin";
}
