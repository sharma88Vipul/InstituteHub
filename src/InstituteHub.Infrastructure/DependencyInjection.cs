using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Tenants;
using InstituteHub.Application.Users;
using InstituteHub.Infrastructure.Identity;
using InstituteHub.Infrastructure.Persistence;
using InstituteHub.Infrastructure.Persistence.Interceptors;
using InstituteHub.Infrastructure.Persistence.Seeding;
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

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));

        // Added in later weeks: Hangfire (week 7), IMessageSender (week 7), IPdfService (week 6).
        return services;
    }
}
