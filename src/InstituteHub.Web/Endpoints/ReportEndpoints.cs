using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Reports;
using InstituteHub.Application.Students;
using InstituteHub.Application.Tenants;
using InstituteHub.Web.Security;

namespace InstituteHub.Web.Endpoints;

/// <summary>File downloads: report CSVs, the student import template and the institute logo.</summary>
public static class ReportEndpoints
{
    private const string CsvType = "text/csv; charset=utf-8";

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/reports/collections.csv", async (DateOnly? from, DateOnly? to, ReportService reports, IClock clock, CancellationToken ct) =>
            {
                var (start, end) = Range(from, to, clock);
                return await reports.CollectionsCsvAsync(start, end, ct) is { } csv
                    ? Results.File(csv, CsvType, $"collections-{start:yyyy-MM-dd}-to-{end:yyyy-MM-dd}.csv")
                    : Results.Forbid();
            })
            .RequireAuthorization(Policies.ViewFinanceReports);

        app.MapGet("/reports/dues.csv", async (ReportService reports, IClock clock, CancellationToken ct) =>
                await reports.DuesCsvAsync(ct) is { } csv
                    ? Results.File(csv, CsvType, $"pending-dues-{clock.Today():yyyy-MM-dd}.csv")
                    : Results.Forbid())
            .RequireAuthorization(Policies.ViewFinanceReports);

        app.MapGet("/reports/attendance.csv", async (DateOnly? from, DateOnly? to, ReportService reports, IClock clock, CancellationToken ct) =>
            {
                var (start, end) = Range(from, to, clock);
                return await reports.AttendanceCsvAsync(start, end, ct) is { } csv
                    ? Results.File(csv, CsvType, $"attendance-{start:yyyy-MM-dd}-to-{end:yyyy-MM-dd}.csv")
                    : Results.Forbid();
            })
            .RequireAuthorization(Policies.ViewAttendanceReports);

        app.MapGet("/students/import-template.csv", () =>
                Results.File(StudentImportService.Template(), CsvType, "students-import-template.csv"))
            .RequireAuthorization(Policies.ManageStudents);

        // The signed-in user's institute logo (settings preview). 404 when none is uploaded.
        app.MapGet("/institute/logo", async (TenantService tenants, HttpResponse response, CancellationToken ct) =>
            {
                if (await tenants.GetLogoAsync(ct) is not { } logo) return Results.NotFound();
                response.Headers.CacheControl = "private, no-cache";
                return Results.File(logo.Bytes, logo.ContentType);
            })
            .RequireAuthorization(Policies.ViewStudents);

        return app;
    }

    /// <summary>Default: the current month up to today.</summary>
    private static (DateOnly Start, DateOnly End) Range(DateOnly? from, DateOnly? to, IClock clock)
    {
        var today = clock.Today();
        return (from ?? new DateOnly(today.Year, today.Month, 1), to ?? today);
    }
}
