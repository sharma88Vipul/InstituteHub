using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace InstituteHub.IntegrationTests.Infrastructure;

/// <summary>Starts a real PostgreSQL 16 container and the web app against it.</summary>
public sealed class PostgresWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("institutehub_tests")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");   // applies migrations + seed on start-up
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Seed:DemoData", "false");
        // Jobs are only queued in tests; each test runs the messaging work itself, so nothing runs twice.
        builder.UseSetting("Hangfire:ServerEnabled", "false");
        builder.UseSetting("App:PublicBaseUrl", "https://test.local");
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = Server; // start the app so migrations run once
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresWebAppFactory>
{
    public const string Name = "postgres";
}
