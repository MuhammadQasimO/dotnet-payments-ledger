using System.Security.Claims;

using Microsoft.Extensions.Options;

using PaymentsLedger.Infrastructure.RateLimiting;

namespace PaymentsLedger.Api.Middleware;

public sealed class RateLimitOptions
{
    public int PerIpPerSecond { get; init; } = 100;
    public int PerUserPerSecond { get; init; } = 50;
}

/// <summary>
/// Two-layer Redis-backed sliding-window rate limiter. Per-IP always applies; per-user
/// applies only when an upstream gateway provided <c>X-User-Id</c>. Standard
/// <c>X-RateLimit-*</c> headers are stamped on every response; on rejection the
/// <c>Retry-After</c> header reflects when the oldest hit ages out.
/// </summary>
public sealed class RateLimitMiddleware(RequestDelegate next, IOptions<RateLimitOptions> options)
{
    private static readonly TimeSpan _window = TimeSpan.FromSeconds(1);
    private readonly RateLimitOptions _opts = options.Value;

    public async Task InvokeAsync(HttpContext context, IRateLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(limiter);

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "anon";
        var ipDecision = await limiter.CheckAsync(
            "ip:" + ip, _opts.PerIpPerSecond, _window, context.RequestAborted);

        RateLimitDecision? userDecision = null;
        var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            userDecision = await limiter.CheckAsync(
                "user:" + userId, _opts.PerUserPerSecond, _window, context.RequestAborted);
        }

        // The tighter of the two governs the visible headers.
        var effective = (userDecision is { Remaining: var ur } && ur < ipDecision.Remaining)
            ? userDecision
            : ipDecision;

        context.Response.Headers["X-RateLimit-Limit"] = effective.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Remaining"] = effective.Remaining.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Reset"] =
            DateTimeOffset.UtcNow.Add(_window).ToUnixTimeSeconds()
                .ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (!ipDecision.Allowed || (userDecision is { Allowed: false }))
        {
            var rejected = !ipDecision.Allowed ? ipDecision : userDecision!;
            var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(rejected.RetryAfter.TotalSeconds));
            context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(
                "{\"status\":429,\"title\":\"Too Many Requests\",\"detail\":\"Rate limit exceeded.\"}",
                context.RequestAborted);
            return;
        }

        await next(context);
    }
}
