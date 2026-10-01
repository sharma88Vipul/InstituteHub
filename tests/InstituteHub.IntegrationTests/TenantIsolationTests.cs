using InstituteHub.Domain.Students;
using InstituteHub.Domain.Tenants;
using InstituteHub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.IntegrationTests;

/// <summary>Design doc 2.3: tenant A can never read or update tenant B data.</summary>
[Collection(PostgresCollection.Name)]
public class TenantIsolationTests(PostgresWebAppFactory factory)
{
    [Fact]
    public async Task Tenant_can_only_read_and_write_its_own_rows()
    {
        var (tenantA, tenantB) = await CreateTwoTenantsAsync();

        Guid studentOfB;
        await using (var asB = new TestScope(factory.Services, tenantB))
        {
            var student = new Student { AdmissionNo = "B-001", FirstName = "Bina", AdmissionDate = new DateOnly(2026, 6, 1) };
            asB.Db.Students.Add(student);         // tenant_id stamped by the interceptor
            await asB.Db.SaveChangesAsync();
            studentOfB = student.Id;
            student.TenantId.ShouldBe(tenantB);
        }

        await using (var asA = new TestScope(factory.Services, tenantA))
        {
            asA.Db.Students.Add(new Student { AdmissionNo = "A-001", FirstName = "Amit", AdmissionDate = new DateOnly(2026, 6, 1) });
            await asA.Db.SaveChangesAsync();

            // Reads are filtered.
            (await asA.Db.Students.AnyAsync(s => s.Id == studentOfB)).ShouldBeFalse();
            (await asA.Db.Students.Select(s => s.AdmissionNo).ToListAsync()).ShouldBe(new[] { "A-001" });

            // Writes to another tenant's row are refused.
            var stolen = await asA.Db.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == studentOfB);
            stolen.FirstName = "Hacked";
            await Should.ThrowAsync<InvalidOperationException>(() => asA.Db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Removing_a_student_soft_deletes_and_writes_an_audit_log()
    {
        var (tenantA, _) = await CreateTwoTenantsAsync();

        await using var asA = new TestScope(factory.Services, tenantA);
        var student = new Student { AdmissionNo = "A-DEL", FirstName = "Temp", AdmissionDate = new DateOnly(2026, 6, 1) };
        asA.Db.Students.Add(student);
        await asA.Db.SaveChangesAsync();

        asA.Db.Students.Remove(student);
        await asA.Db.SaveChangesAsync();

        (await asA.Db.Students.AnyAsync(s => s.Id == student.Id)).ShouldBeFalse();
        (await asA.Db.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == student.Id)).IsDeleted.ShouldBeTrue();
        (await asA.Db.AuditLogs.Where(a => a.EntityId == student.Id).Select(a => a.Action.ToString()).ToListAsync())
            .ShouldBe(new[] { "Create", "Delete" }, ignoreOrder: true);
    }

    private async Task<(Guid A, Guid B)> CreateTwoTenantsAsync()
    {
        await using var platform = new TestScope(factory.Services, tenantId: null);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var a = new Tenant { Name = "Alpha Classes", Slug = $"alpha-{suffix}", OwnerName = "A", Phone = "+919000000001", ReceiptPrefix = "ALP" };
        var b = new Tenant { Name = "Beta Academy", Slug = $"beta-{suffix}", OwnerName = "B", Phone = "+919000000002", ReceiptPrefix = "BET" };
        platform.Db.Tenants.AddRange(a, b);
        await platform.Db.SaveChangesAsync();
        return (a.Id, b.Id);
    }
}
