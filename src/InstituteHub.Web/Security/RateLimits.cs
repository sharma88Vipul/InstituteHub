using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace InstituteHub.Web.Security;

public static class RateLimits
{
    /// <summary>Limits form posts to /Account/* (login, register, password reset) to 10 per minute per IP.</summary>
    public static void Configure(RateLimiterOptions o)
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        {
            var isAccountPost = HttpMethods.IsPost(ctx.Request.Method)
                                && ctx.Request.Path.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase);
            if (!isAccountPost) return RateLimitPartition.GetNoLimiter("none");

            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter($"account:{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
        });
    }
}
