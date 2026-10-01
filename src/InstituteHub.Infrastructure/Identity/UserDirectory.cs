using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Common;
using InstituteHub.Application.Users;
using InstituteHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Infrastructure.Identity;

/// <summary>
/// Identity users are not covered by the tenant query filters, so every query here filters on
/// the current tenant explicitly.
/// </summary>
public sealed class UserDirectory(AppDbContext db, ITenantProvider tenant) : IUserDirectory
{
    public async Task<IReadOnlyList<UserOption>> GetTeachersAsync(CancellationToken ct = default)
    {
        if (tenant.CurrentTenantId is not { } tenantId) return [];

        var roleIds = await db.Roles.AsNoTracking()
            .Where(r => r.Name == Roles.Teacher || r.Name == Roles.Owner)
            .Select(r => r.Id)
            .ToListAsync(ct);

        return await db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsActive
                        && db.UserRoles.Any(ur => ur.UserId == u.Id && roleIds.Contains(ur.RoleId)))
            .OrderBy(u => u.FullName)
            .Select(u => new UserOption(u.Id, u.FullName != "" ? u.FullName : u.Email ?? u.UserName ?? "User"))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0 || tenant.CurrentTenantId is not { } tenantId) return new Dictionary<Guid, string>();

        return await db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName != "" ? u.FullName : u.Email ?? "User", ct);
    }
}
