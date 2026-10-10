using Hangfire;
using InstituteHub.Application;
using InstituteHub.Infrastructure;
using InstituteHub.Infrastructure.Identity;
using InstituteHub.Infrastructure.Jobs;
using InstituteHub.Infrastructure.Persistence;
using InstituteHub.Infrastructure.Persistence.Seeding;
using InstituteHub.Web.Components;
using InstituteHub.Web.Components.Account;
using InstituteHub.Web.Endpoints;
using InstituteHub.Web.Logging;
using InstituteHub.Web.Monitoring;
using InstituteHub.Web.Security;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor.Services;
using Serilog;

// Start-up logger until the host builds the configured Serilog logger.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Logging: Serilog, configured from appsettings ("Serilog" section).
    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Layers
    builder.Services
        .AddApplication()                          // use-case services + validators
        .AddInfrastructure(builder.Configuration); // DbContext, tenant provider, clock, seeding

    // UI
    builder.Services.AddMudServices();
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // Blazor Server: make the signed-in user (and tenant) available to scoped services inside circuits.
    builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<CircuitHandler, UserCircuitHandler>());

    // Identity (cookie auth) on PostgreSQL via AppDbContext
    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddScoped<IdentityRedirectManager>();
    builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        })
        .AddIdentityCookies();

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    });

    builder.Services.AddIdentityCore<AppUser>(options =>
        {
            options.SignIn.RequireConfirmedAccount = true;
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            options.User.RequireUniqueEmail = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddRoles<AppRole>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders()
        .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>();

    builder.Services.AddSingleton<IEmailSender<AppUser>, IdentityNoOpEmailSender>();

    // Persist Data Protection keys so sign-ins survive restarts and redeploys (design doc 7.1).
    // In Docker, point DataProtection:KeysPath at a mounted volume.
    var dataProtection = builder.Services.AddDataProtection().SetApplicationName("InstituteHub");
    if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    {
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
    }
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();

    // Authorization, rate limiting, health
    builder.Services.AddAuthorization(AuthorizationPolicies.Configure);
    builder.Services.AddRateLimiter(RateLimits.Configure);
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
        .AddCheck<HangfireHealthCheck>("background-jobs", tags: ["ready"]);

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseMigrationsEndPoint();
    }
    else
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();

    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<LogContextEnrichmentMiddleware>();
    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    // Additional endpoints required by the Identity /Account Razor components.
    app.MapAdditionalIdentityEndpoints();
    app.MapHealthEndpoints();
    app.MapReceiptEndpoints();
    app.MapWebhookEndpoints();
    app.MapReportEndpoints();

    // Hangfire dashboard for the platform admin only (design doc 5.4). The endpoint policy does the check, so
    // Hangfire's own "local requests only" filter is switched off.
    app.MapHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [],
            DashboardTitle = "InstituteHub jobs",
            AppPath = "/dashboard",
            DisplayStorageConnectionString = false,
        })
        .RequireAuthorization(Policies.PlatformAdmin);

    // Development applies migrations automatically. On a server, run the EF migration bundle before starting the new
    // version (deploy/README.md), or set Database:MigrateOnStartup=true for a single-instance deployment.
    await DbSeeder.SeedAsync(app.Services,
        applyMigrations: app.Environment.IsDevelopment() || builder.Configuration.GetValue("Database:MigrateOnStartup", false));

    // Daily MonthlyDueJob (00:30) and FeeReminderJob (10:00), Indian time.
    RecurringJobs.Register(app.Services);

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "InstituteHub terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

