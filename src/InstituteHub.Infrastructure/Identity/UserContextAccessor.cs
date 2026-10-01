using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace InstituteHub.Infrastructure.Identity;

/// <summary>
/// Holds the signed-in user for the current DI scope.
/// In an HTTP request it falls back to HttpContext.User. In an interactive Blazor Server
/// circuit HttpContext is not available, so the Web project's circuit handler sets <see cref="User"/>.
/// </summary>
public sealed class UserContextAccessor(IHttpContextAccessor http)
{
    private ClaimsPrincipal? _user;

    public ClaimsPrincipal? User
    {
        get => _user ?? http.HttpContext?.User;
        set => _user = value;
    }
}
