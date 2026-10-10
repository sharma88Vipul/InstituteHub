using InstituteHub.Domain.Tenants;
using InstituteHub.Infrastructure.Identity;
using InstituteHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InstituteHub.Infrastructure.Jobs;

/// <summary>
/// Jobs run outside an HTTP request (design doc 5.5): each piece of work gets its own DI scope with the institute set
/// on JobTenantContext, so query filters and the write guard apply exactly as for a signed-in user of that institute.
/// </summary>
public sealed class TenantJobRunner(IServiceScopeFactory scopes, ILogger<TenantJobRunner> logger)
{
    public async Task<T> RunAsync<T>(Guid tenantId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<JobTenantContext>().TenantId = tenantId;
        using (logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId }))
        {
            return await work(scope.ServiceProvider);
        }
    }

    /// <summary>
    /// Runs the work for every institute on trial or active, one scope each. One institute failing does not stop the
    /// others; the job is reported as failed at the end so Hangfire retries it (the work must be safe to repeat).
    /// </summary>
    public async Task ForEachActiveTenantAsync(
        string jobName, Func<IServiceProvider, CancellationToken, Task<string>> work, CancellationToken ct)
    {
        List<Guid> tenantIds;
        await using (var scope = scopes.CreateAsyncScope())
        {
            // No tenant context here: tenants are not tenant-filtered, so this lists every institute.
            tenantIds = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.AsNoTracking()
                .Where(t => t.Status == TenantStatus.Trial || t.Status == TenantStatus.Active)
                .Select(t => t.Id)
                .ToListAsync(ct);
        }

        var failures = 0;
        foreach (var tenantId in tenantIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var summary = await RunAsync(tenantId, sp => work(sp, ct));
                logger.LogInformation("{Job} for institute {TenantId}: {Summary}", jobName, tenantId, summary);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                logger.LogError(ex, "{Job} failed for institute {TenantId}", jobName, tenantId);
            }
        }

        logger.LogInformation("{Job} finished for {Count} institute(s), {Failures} failed", jobName, tenantIds.Count, failures);
        if (failures > 0)
        {
            throw new InvalidOperationException($"{jobName} failed for {failures} of {tenantIds.Count} institute(s); see the log.");
        }
    }
}
