using InstituteHub.Application.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace InstituteHub.Infrastructure.Persistence;

/// <summary>
/// Used by "dotnet ef" to create migrations without starting the web app.
/// Connection string: env var ConnectionStrings__Default, else the local database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                 ?? "Host=localhost;Port=5432;Database=institutehub;Username=institutehub;Password=institutehub_dev";

        // The Identity model depends on IdentityOptions.Stores.SchemaVersion (Version3 adds the passkeys table).
        // It must match Program.cs, otherwise migrations differ from the runtime model
        // and Migrate() fails with PendingModelChangesWarning.
        var identityServices = new ServiceCollection()
            .Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs)
            .UseSnakeCaseNamingConvention()
            .UseApplicationServiceProvider(identityServices)
            .Options;

        return new AppDbContext(options, new NoTenant());
    }

    private sealed class NoTenant : ITenantProvider
    {
        public Guid? CurrentTenantId => null;
    }
}
