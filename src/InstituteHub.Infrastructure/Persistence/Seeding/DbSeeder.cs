using InstituteHub.Application.Common;
using InstituteHub.Domain.Billing;
using InstituteHub.Domain.Messaging;
using InstituteHub.Domain.Students;
using InstituteHub.Domain.Tenants;
using InstituteHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InstituteHub.Infrastructure.Persistence.Seeding;

/// <summary>Idempotent seed data (design doc 4.6). Safe to run on every start-up.</summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, bool applyMigrations, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbSeeder));
        var options = sp.GetRequiredService<IOptions<SeedOptions>>().Value;

        if (applyMigrations)
        {
            if (!db.Database.GetMigrations().Any())
            {
                logger.LogWarning("No EF Core migrations found. Run: dotnet ef migrations add InitialCreate (see README). Skipping seeding.");
                return;
            }
            await db.Database.MigrateAsync(ct);
        }

        await SeedRolesAsync(sp.GetRequiredService<RoleManager<AppRole>>());
        await SeedSubscriptionPlansAsync(db, ct);
        await SeedMessageTemplatesAsync(db, ct);
        await SeedPlatformAdminAsync(sp.GetRequiredService<UserManager<AppUser>>(), options, logger);

        if (options.DemoData)
        {
            await SeedDemoTenantAsync(db, sp.GetRequiredService<UserManager<AppUser>>(), options, logger, ct);
        }
    }

    private static async Task SeedRolesAsync(RoleManager<AppRole> roles)
    {
        foreach (var name in Roles.All)
        {
            if (!await roles.RoleExistsAsync(name))
            {
                await roles.CreateAsync(new AppRole(name));
            }
        }
    }

    private static async Task SeedSubscriptionPlansAsync(AppDbContext db, CancellationToken ct)
    {
        SubscriptionPlan[] plans =
        [
            new() { Code = "STARTER", Name = "Starter", MaxStudents = 75,  PriceMonthly = 499m,  PriceYearly = 4990m,  Features = """{"reminders": true, "sms": false}""" },
            new() { Code = "GROWTH",  Name = "Growth",  MaxStudents = 250, PriceMonthly = 999m,  PriceYearly = 9990m,  Features = """{"reminders": true, "sms": true}""" },
            new() { Code = "PRO",     Name = "Pro",     MaxStudents = 750, PriceMonthly = 1999m, PriceYearly = 19990m, Features = """{"reminders": true, "sms": true, "absent_alerts": true}""" },
        ];

        var existing = await db.SubscriptionPlans.IgnoreQueryFilters().Select(p => p.Code).ToListAsync(ct);
        db.SubscriptionPlans.AddRange(plans.Where(p => !existing.Contains(p.Code)));
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedMessageTemplatesAsync(AppDbContext db, CancellationToken ct)
    {
        const string reminder = "Dear {{guardian}}, a fee of Rs {{amount}} for {{student}} is due on {{due_date}}. - {{institute}}";
        const string overdue = "Dear {{guardian}}, the fee of Rs {{amount}} for {{student}} was due on {{due_date}} and is still pending. Please pay at the earliest. - {{institute}}";
        const string receipt = "Dear {{guardian}}, we received Rs {{amount}} for {{student}}. Receipt {{receipt_no}}: {{receipt_link}} - {{institute}}";
        const string absent = "Dear {{guardian}}, {{student}} was absent from {{batch}} on {{date}}. - {{institute}}";

        // WhatsApp for guardians who opted in; SMS is the fallback (week 7). Provider template ids (approved WhatsApp
        // template names / DLT ids) are filled in when a real provider is connected.
        MessageTemplate[] templates =
        [
            new() { Code = MessageTemplateCodes.FeeReminder, Channel = MessageChannel.WhatsApp, Body = reminder },
            new() { Code = MessageTemplateCodes.FeeOverdue, Channel = MessageChannel.WhatsApp, Body = overdue },
            new() { Code = MessageTemplateCodes.PaymentReceipt, Channel = MessageChannel.WhatsApp, Body = receipt },
            new() { Code = MessageTemplateCodes.AbsentAlert, Channel = MessageChannel.WhatsApp, Body = absent },
            new() { Code = MessageTemplateCodes.FeeReminder, Channel = MessageChannel.Sms, Body = reminder },
            new() { Code = MessageTemplateCodes.FeeOverdue, Channel = MessageChannel.Sms, Body = overdue },
            new() { Code = MessageTemplateCodes.PaymentReceipt, Channel = MessageChannel.Sms, Body = receipt },
            new() { Code = MessageTemplateCodes.AbsentAlert, Channel = MessageChannel.Sms, Body = absent },
        ];

        var existing = (await db.MessageTemplates.IgnoreQueryFilters()
                .Where(t => t.TenantId == null)
                .Select(t => new { t.Code, t.Channel })
                .ToListAsync(ct))
            .Select(t => (t.Code, t.Channel))
            .ToHashSet();
        db.MessageTemplates.AddRange(templates.Where(t => !existing.Contains((t.Code, t.Channel))));
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedPlatformAdminAsync(UserManager<AppUser> users, SeedOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.PlatformAdminEmail) || string.IsNullOrWhiteSpace(options.PlatformAdminPassword))
        {
            logger.LogWarning("Seed:PlatformAdminEmail / Seed:PlatformAdminPassword not configured; platform admin not created.");
            return;
        }

        if (await users.FindByEmailAsync(options.PlatformAdminEmail) is not null) return;

        var admin = new AppUser
        {
            UserName = options.PlatformAdminEmail,
            Email = options.PlatformAdminEmail,
            EmailConfirmed = true,
            FullName = "Platform Admin",
        };
        await CreateUserAsync(users, admin, options.PlatformAdminPassword, Roles.PlatformAdmin, logger);
    }

    private static async Task SeedDemoTenantAsync(
        AppDbContext db, UserManager<AppUser> users, SeedOptions options, ILogger logger, CancellationToken ct)
    {
        const string slug = "demo";
        var demo = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (demo is null)
        {
            demo = new Tenant
            {
                Name = "Demo Coaching Centre",
                Slug = slug,
                OwnerName = "Demo Owner",
                Phone = "+919800000000",
                City = "Chandigarh",
                State = "Punjab",
                ReceiptPrefix = "DCC",
                Status = TenantStatus.Trial,
                TrialEndsAt = DateTimeOffset.UtcNow.AddDays(14),
            };
            db.Tenants.Add(demo);
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Students.IgnoreQueryFilters().AnyAsync(s => s.TenantId == demo.Id, ct))
        {
            string[] first = ["Aarav", "Vivaan", "Aditya", "Vihaan", "Arjun", "Sai", "Reyansh", "Ayaan", "Krishna", "Ishaan",
                              "Ananya", "Diya", "Aadhya", "Saanvi", "Pari", "Myra", "Anika", "Navya", "Kiara", "Riya"];
            string[] last = ["Sharma", "Verma", "Gupta", "Singh", "Kumar", "Mehta", "Bansal", "Kaur", "Joshi", "Malhotra"];
            string[] grades = ["Class 9", "Class 10", "Class 11", "Class 12", "Dropper"];
            var random = new Random(42);

            for (var i = 1; i <= 30; i++)
            {
                var lastName = last[random.Next(last.Length)];
                var student = new Student
                {
                    TenantId = demo.Id,
                    AdmissionNo = $"DCC{i:D4}",
                    FirstName = first[random.Next(first.Length)],
                    LastName = lastName,
                    ClassGrade = grades[random.Next(grades.Length)],
                    AdmissionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-random.Next(10, 200))),
                };
                var guardian = new Guardian
                {
                    TenantId = demo.Id,
                    FullName = $"Mr. {lastName}",
                    Relation = GuardianRelation.Father,
                    Phone = $"+9198{random.Next(10000000, 99999999)}",
                    WhatsAppOptIn = true,
                    OptInAt = DateTimeOffset.UtcNow,
                };
                db.Students.Add(student);
                db.Guardians.Add(guardian);
                db.StudentGuardians.Add(new StudentGuardian
                {
                    TenantId = demo.Id, StudentId = student.Id, GuardianId = guardian.Id, IsPrimary = true,
                });
            }
            await db.SaveChangesAsync(ct);
        }

        if (!string.IsNullOrWhiteSpace(options.DemoOwnerEmail) && !string.IsNullOrWhiteSpace(options.DemoOwnerPassword)
            && await users.FindByEmailAsync(options.DemoOwnerEmail) is null)
        {
            var owner = new AppUser
            {
                UserName = options.DemoOwnerEmail,
                Email = options.DemoOwnerEmail,
                EmailConfirmed = true,
                FullName = demo.OwnerName,
                TenantId = demo.Id,
            };
            await CreateUserAsync(users, owner, options.DemoOwnerPassword, Roles.Owner, logger);
        }
    }

    private static async Task CreateUserAsync(UserManager<AppUser> users, AppUser user, string password, string role, ILogger logger)
    {
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError("Could not create seed user {Email}: {Errors}", user.Email,
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }
        await users.AddToRoleAsync(user, role);
        logger.LogInformation("Seeded {Role} user {Email}", role, user.Email);
    }
}
