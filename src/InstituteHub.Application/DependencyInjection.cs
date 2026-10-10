using FluentValidation;
using InstituteHub.Application.Attendance;
using InstituteHub.Application.Batches;
using InstituteHub.Application.Billing;
using InstituteHub.Application.Fees;
using InstituteHub.Application.Messaging;
using InstituteHub.Application.Payments;
using InstituteHub.Application.Reports;
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
        services.AddScoped<PaymentService>();
        services.AddScoped<AttendanceService>();
        services.AddScoped<MessagingService>();
        services.AddScoped<ReportService>();
        services.AddScoped<StudentImportService>();

        // Plan limits and feature flags, cached per institute for 5 minutes.
        services.AddMemoryCache();
        services.AddScoped<IFeatureService, FeatureService>();

        return services;
    }
}
