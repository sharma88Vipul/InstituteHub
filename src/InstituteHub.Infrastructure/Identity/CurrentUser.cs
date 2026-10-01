using System.Security.Claims;
using InstituteHub.Application.Abstractions;

namespace InstituteHub.Infrastructure.Identity;

public sealed class CurrentUser(UserContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(accessor.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public bool IsAuthenticated => accessor.User?.Identity?.IsAuthenticated == true;

    public bool IsInRole(string role) => accessor.User?.IsInRole(role) == true;
}
