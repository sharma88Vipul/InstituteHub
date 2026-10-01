using InstituteHub.Application.Abstractions;
using Serilog.Context;

namespace InstituteHub.Web.Logging;

/// <summary>Adds TenantId and UserId to every log line written during a request.</summary>
public sealed class LogContextEnrichmentMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantProvider tenant, ICurrentUser user)
    {
        using (LogContext.PushProperty("TenantId", tenant.CurrentTenantId))
        using (LogContext.PushProperty("UserId", user.UserId))
        {
            await next(context);
        }
    }
}
