using System.Security.Claims;
using InstituteHub.Application.Common;
using InstituteHub.Infrastructure.Identity;
using InstituteHub.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace InstituteHub.IntegrationTests.Infrastructure;

/// <summary>
/// A DI scope acting as a given tenant (like a background job would), optionally signed in
/// as a user with roles so role checks inside services apply.
/// </summary>
public sealed class TestScope : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;

    public TestScope(IServiceProvider services, Guid? tenantId, Guid? userId = null, params string[] roles)
    {
        _scope = services.CreateAsyncScope();
        _scope.ServiceProvider.GetRequiredService<JobTenantContext>().TenantId = tenantId;

        if (userId is not null || roles.Length > 0)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, (userId ?? Guid.CreateVersion7()).ToString()) };
            if (tenantId is { } t) claims.Add(new Claim(AppClaimTypes.TenantId, t.ToString()));
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            _scope.ServiceProvider.GetRequiredService<UserContextAccessor>().User =
                new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
        }

        Db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    /// <summary>Acting as the institute owner.</summary>
    public static TestScope AsOwner(IServiceProvider services, Guid tenantId) =>
        new(services, tenantId, null, Roles.Owner);

    public AppDbContext Db { get; }

    public IServiceProvider Services => _scope.ServiceProvider;

    public T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    public ValueTask DisposeAsync() => _scope.DisposeAsync();
}
