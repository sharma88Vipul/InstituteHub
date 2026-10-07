using FluentValidation;
using InstituteHub.Application.Attendance;
using InstituteHub.Application.Batches;
using InstituteHub.Application.Fees;
using InstituteHub.Application.Students;
using InstituteHub.Application.Tenants;
using Microsoft.Extensions.DependencyInjection;

namespace InstituteHub.Application;

public static class DependencyInjection
{
    /// <summary>Registers use-case services and FluentValidation validators.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // Feature services
        services.AddScoped<TenantService>();
        services.AddScoped<StudentService>();
        services.AddScoped<BatchService>();
        services.AddScoped<EnrollmentService>();
        services.AddScoped<FeePlanService>();
        services.AddScoped<FeeDueService>();
        services.AddScoped<AttendanceService>();

        return services;
    }
}
