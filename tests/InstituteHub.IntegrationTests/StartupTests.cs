using System.Net;
using InstituteHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class StartupTests(PostgresWebAppFactory factory)
{
    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // JSON: {"status":"Healthy","checks":{"database":…,"background-jobs":…}}
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"status\":\"Healthy\"");
        body.ShouldContain("background-jobs");

        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Migrations_are_applied_and_tables_use_snake_case()
    {
        await using var scope = new TestScope(factory.Services, tenantId: null);

        (await scope.Db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();

        var tables = await scope.Db.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync();

        tables.ShouldContain("fee_dues");
        tables.ShouldContain("payment_allocations");
        tables.ShouldContain("asp_net_users");
    }

    [Fact]
    public async Task Seed_data_is_present()
    {
        await using var scope = new TestScope(factory.Services, tenantId: null);

        (await scope.Db.SubscriptionPlans.Select(p => p.Code).ToListAsync())
            .ShouldBe(new[] { "GROWTH", "PRO", "STARTER" }, ignoreOrder: true);
        (await scope.Db.Roles.CountAsync()).ShouldBe(4);
    }
}
