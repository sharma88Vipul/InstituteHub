using Hangfire;
using Hangfire.PostgreSql;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Tenants;
using InstituteHub.Application.Users;
using InstituteHub.Infrastructure.Identity;
using InstituteHub.Infrastructure.Jobs;
using InstituteHub.Infrastructure.Messaging;
using InstituteHub.Infrastructure.Pdf;
using InstituteHub.Infrastructure.Persistence;
using InstituteHub.Infrastructure.Persistence.Interceptors;
using InstituteHub.Infrastructure.Persistence.Seeding;
using InstituteHub.Infrastructure.Storage;
using InstituteHub.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InstituteHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");

        // Current user / tenant resolution
        services.AddHttpContextAccessor();
        services.AddScoped<UserContextAccessor>();
        services.AddScoped<JobTenantContext>();
        services.AddScoped<ITenantProvider, TenantProvider>();
        services.AddScoped<ICurrentUser, CurrentUser>();

        // Time
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();

        // Persistence
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Sign-up (needs Identity's UserManager, so it lives here rather than in Application)
        services.AddScoped<ISignUpService, SignUpService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IUserManagement, UserManagementService>();

        // Uploaded files (logos) on local disk / Docker volume.
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        // Documents
        services.AddSingleton<IPdfService, QuestPdfService>();

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));

        AddMessaging(services, configuration);
        AddBackgroundJobs(services, configuration, connectionString);

        return services;
    }

    /// <summary>WhatsApp/SMS sender and public receipt links (design doc 5.4 / 6.6).</summary>
    private static void AddMessaging(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MessagingOptions.SectionName);
        services.Configure<MessagingOptions>(section);
        services.Configure<AppUrlOptions>(configuration.GetSection(AppUrlOptions.SectionName));

        var provider = section[nameof(MessagingOptions.Provider)] ?? "Fake";
        if (!provider.Equals("Fake", StringComparison.OrdinalIgnoreCase))
        {
            // A real provider (Gupshup/Interakt/Twilio) is added when the institute's WhatsApp Business account is ready.
            throw new InvalidOperationException(
                $"Messaging:Provider '{provider}' is not supported yet. Use 'Fake' until a WhatsApp/SMS provider is integrated.");
        }
        services.AddScoped<IMessageSender, FakeMessageSender>();

        services.AddDataProtection();
        services.AddSingleton<IReceiptLinks, ReceiptLinks>();
    }

    /// <summary>
    /// Hangfire on PostgreSQL (its own "hangfire" schema, created on first start). The processing server can be
    /// switched off with Hangfire:ServerEnabled=false (integration tests); jobs are then only queued.
    /// </summary>
    private static void AddBackgroundJobs(IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                options => options.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions
                {
                    SchemaName = "hangfire",
                    PrepareSchemaIfNecessary = true,
                    QueuePollInterval = TimeSpan.FromSeconds(5),
                }));

        if (configuration.GetValue("Hangfire:ServerEnabled", true))
        {
            services.AddHangfireServer(options =>
            {
                options.WorkerCount = Math.Clamp(Environment.ProcessorCount, 2, 5);
                options.Queues = ["default"];
            });
        }

        services.AddSingleton<TenantJobRunner>();
        services.AddScoped<MonthlyDueJob>();
        services.AddScoped<FeeReminderJob>();
        services.AddScoped<SendReceiptJob>();
        services.AddScoped<AbsentAlertJob>();
        services.AddScoped<IBackgroundJobs, HangfireBackgroundJobs>();
    }
}
