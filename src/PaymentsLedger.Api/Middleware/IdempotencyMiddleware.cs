using System.Security.Cryptography;
using System.Text;

using PaymentsLedger.Infrastructure.Idempotency;

namespace PaymentsLedger.Api.Middleware;

/// <summary>
/// Stripe-style HTTP idempotency on POST. Required header <c>Idempotency-Key</c>; the
/// SHA-256 of the request body is stored alongside the cached response. Replay with the
/// same key + same body returns the cached response; same key + different body returns
/// 409 to surface client bugs (most demos silently dedupe — that hides bugs).
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
{
    public const string HeaderName = "Idempotency-Key";
    public const string IdempotencyKeyItem = "_idempotency_key";

    private static readonly TimeSpan _ttl = TimeSpan.FromHours(24);

    public async Task InvokeAsync(HttpContext context, IIdempotencyStore store)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var keyHeader)
            || string.IsNullOrWhiteSpace(keyHeader.ToString()))
        {
            await Write400Async(context, "Missing required header 'Idempotency-Key'.");
            return;
        }

        var key = keyHeader.ToString();
        context.Items[IdempotencyKeyItem] = key;

        context.Request.EnableBuffering();
        var requestHash = await ComputeBodyHashAsync(context.Request, context.RequestAborted);
        context.Request.Body.Position = 0;

        var existing = await store.GetAsync(key, context.RequestAborted);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Idempotency key {Key} reused with a different body (hash {Old} != {New}).",
                    key, existing.RequestHash, requestHash);
                await Write409Async(context, key);
                return;
            }

            // Same key + same body → replay cached response.
            context.Response.StatusCode = existing.StatusCode;
            if (!string.IsNullOrWhiteSpace(existing.ContentType))
            {
                context.Response.ContentType = existing.ContentType!;
            }
            context.Response.Headers["Idempotent-Replay"] = "true";
            await context.Response.WriteAsync(existing.ResponseBody, context.RequestAborted);
            return;
        }

        // Buffer the response so we can persist it for future replays.
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);

            buffer.Position = 0;
            var responseBody = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync(context.RequestAborted);

            // Only cache deterministic successes; let 4xx/5xx be retried with a fresh attempt.
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                await store.PutAsync(
                    key,
                    new IdempotencyRecord(
                        requestHash,
                        context.Response.StatusCode,
                        context.Response.ContentType,
                        responseBody),
                    _ttl,
                    context.RequestAborted);
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static async Task<string> ComputeBodyHashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.Body.Position = 0;
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(request.Body, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static Task Write400Async(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsync(
            $"{{\"status\":400,\"title\":\"Bad Request\",\"detail\":\"{message}\"}}",
            context.RequestAborted);
    }

    private static Task Write409Async(HttpContext context, string key)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsync(
            $"{{\"status\":409,\"title\":\"Idempotency conflict\",\"detail\":\"Idempotency-Key '{key}' was previously used with a different request body.\"}}",
            context.RequestAborted);
    }
}
