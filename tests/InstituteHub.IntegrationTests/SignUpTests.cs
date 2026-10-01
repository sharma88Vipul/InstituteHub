using InstituteHub.Application.Common;
using InstituteHub.Application.Tenants;
using InstituteHub.Domain.Billing;
using InstituteHub.Domain.Students;
using InstituteHub.Domain.Tenants;
using InstituteHub.Infrastructure.Identity;
using InstituteHub.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.IntegrationTests;

/// <summary>Week 2 "done when": two institutes can sign up and cannot see each other.</summary>
[Collection(PostgresCollection.Name)]
public class SignUpTests(PostgresWebAppFactory factory)
{
    private static SignUpRequest NewRequest(string institute) => new(
        institute, "Owner Name", "98765 43210", $"owner-{Guid.NewGuid():N}@example.com", "Secret#1234");

    private async Task<SignUpResult> SignUpAsync(SignUpRequest request)
    {
        await using var anonymous = new TestScope(factory.Services, tenantId: null);
        var result = await anonymous.Get<ISignUpService>().SignUpAsync(request, CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        return result.Value;
    }

    [Fact]
    public async Task Sign_up_creates_trial_tenant_owner_and_subscription()
    {
        var request = NewRequest("Sharma Science Classes");
        var signUp = await SignUpAsync(request);

        await using var platform = new TestScope(factory.Services, tenantId: null);
        var tenant = await platform.Db.Tenants.SingleAsync(t => t.Id == signUp.TenantId);
        tenant.Status.ShouldBe(TenantStatus.Trial);
        tenant.TrialEndsAt.ShouldNotBeNull();
        tenant.Phone.ShouldBe("+919876543210");
        tenant.ReceiptPrefix.ShouldBe("SSC");
        tenant.Slug.ShouldStartWith("sharma-science-classes");

        var subscription = await platform.Db.TenantSubscriptions.IgnoreQueryFilters()
            .Include(s => s.Plan)
            .SingleAsync(s => s.TenantId == signUp.TenantId);
        subscription.Status.ShouldBe(SubscriptionStatus.Trial);
        subscription.Plan!.Code.ShouldBe("GROWTH");

        var users = platform.Get<UserManager<AppUser>>();
        var owner = await users.FindByIdAsync(signUp.UserId.ToString());
        owner.ShouldNotBeNull();
        owner.TenantId.ShouldBe(signUp.TenantId);
        (await users.IsInRoleAsync(owner, Roles.Owner)).ShouldBeTrue();

        // The auth cookie carries the tenant_id claim.
        var principal = await platform.Get<IUserClaimsPrincipalFactory<AppUser>>().CreateAsync(owner);
        principal.FindFirst(AppClaimTypes.TenantId)!.Value.ShouldBe(signUp.TenantId.ToString());
    }

    [Fact]
    public async Task Same_institute_name_gets_a_unique_slug()
    {
        var name = $"Excel Academy {Guid.NewGuid():N}"[..24];
        var first = await SignUpAsync(NewRequest(name));
        var second = await SignUpAsync(NewRequest(name));

        await using var platform = new TestScope(factory.Services, tenantId: null);
        var slugs = await platform.Db.Tenants
            .Where(t => t.Id == first.TenantId || t.Id == second.TenantId)
            .Select(t => t.Slug)
            .ToListAsync();
        slugs.Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_and_nothing_is_created()
    {
        var request = NewRequest("First Institute");
        await SignUpAsync(request);

        await using var anonymous = new TestScope(factory.Services, tenantId: null);
        var tenantsBefore = await anonymous.Db.Tenants.CountAsync();

        var again = await anonymous.Get<ISignUpService>()
            .SignUpAsync(request with { InstituteName = "Second Institute" }, CancellationToken.None);

        again.IsFailure.ShouldBeTrue();
        again.FirstError!.Code.ShouldBe("Email.Taken");
        (await anonymous.Db.Tenants.CountAsync()).ShouldBe(tenantsBefore);
    }

    [Fact]
    public async Task Weak_password_rolls_back_the_tenant()
    {
        await using var anonymous = new TestScope(factory.Services, tenantId: null);
        var tenantsBefore = await anonymous.Db.Tenants.CountAsync();

        // Passes the 8-character rule but fails Identity's complexity rules (no digit/upper/symbol).
        var result = await anonymous.Get<ISignUpService>()
            .SignUpAsync(NewRequest("Rollback Classes") with { Password = "alllowercase" }, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        (await anonymous.Db.Tenants.CountAsync()).ShouldBe(tenantsBefore);
    }

    [Fact]
    public async Task Two_signed_up_institutes_cannot_see_each_other()
    {
        var a = await SignUpAsync(NewRequest("Alpha Tutorials"));
        var b = await SignUpAsync(NewRequest("Beta Coaching"));

        await using (var asA = new TestScope(factory.Services, a.TenantId))
        {
            asA.Db.Students.Add(new Student { AdmissionNo = "A1", FirstName = "Asha", AdmissionDate = new DateOnly(2026, 10, 1) });
            await asA.Db.SaveChangesAsync();
            (await asA.Get<TenantService>().GetCurrentAsync())!.Name.ShouldBe("Alpha Tutorials");
        }

        await using var asB = new TestScope(factory.Services, b.TenantId);
        (await asB.Db.Students.CountAsync()).ShouldBe(0);
        (await asB.Db.TenantSubscriptions.CountAsync()).ShouldBe(1);
        (await asB.Get<TenantService>().GetCurrentAsync())!.Name.ShouldBe("Beta Coaching");
        (await asB.Get<TenantService>().GetOnboardingChecklistAsync())!.StudentCount.ShouldBe(0);
    }
}
