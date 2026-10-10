using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InstituteHub.Web.Monitoring;

/// <summary>
/// Background jobs are healthy when a Hangfire server has sent a heartbeat recently. Many failed jobs make the
/// check "degraded" (still HTTP 200) so the uptime monitor can warn without paging.
/// </summary>
public sealed class HangfireHealthCheck(JobStorage storage, IConfiguration configuration) : IHealthCheck
{
    private const int FailedJobsWarning = 20;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var monitoring = storage.GetMonitoringApi();
            var stats = monitoring.GetStatistics();
            var data = new Dictionary<string, object>
            {
                ["servers"] = stats.Servers,
                ["enqueued"] = stats.Enqueued,
                ["failed"] = stats.Failed,
            };

            if (configuration.GetValue("Hangfire:ServerEnabled", true))
            {
                var alive = monitoring.Servers().Any(s => s.Heartbeat is { } beat && DateTime.UtcNow - beat < TimeSpan.FromMinutes(2));
                if (!alive) return Task.FromResult(HealthCheckResult.Unhealthy("No Hangfire server heartbeat in the last 2 minutes.", data: data));
            }

            return Task.FromResult(stats.Failed > FailedJobsWarning
                ? HealthCheckResult.Degraded($"{stats.Failed} failed background jobs – check /hangfire.", data: data)
                : HealthCheckResult.Healthy("Background jobs are running.", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Hangfire storage is not reachable.", ex));
        }
    }
}

public static class HealthEndpoints
{
    /// <summary>
    /// /health/live – the process answers (container restart probe).
    /// /health/ready and /health – database and background jobs (uptime monitor), JSON with each check's status.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

        var ready = new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = WriteJsonAsync };
        app.MapHealthChecks("/health/ready", ready);
        app.MapHealthChecks("/health", ready);
        return app;
    }

    private static Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new
        {
            status = report.Status.ToString(),
            durationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(e => e.Key, e => new
            {
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = (int)e.Value.Duration.TotalMilliseconds,
            }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
