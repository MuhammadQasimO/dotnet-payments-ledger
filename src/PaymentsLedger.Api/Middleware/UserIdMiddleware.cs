using System.Security.Claims;

namespace PaymentsLedger.Api.Middleware;

/// <summary>
/// Trusts the <c>X-User-Id</c> header set by an upstream gateway (Kong/Envoy/API
/// Gateway) and populates <see cref="HttpContext.User"/>. The README documents the
/// "auth is upstream" assumption — this service does not authenticate.
/// </summary>
public sealed class UserIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-User-Id";
    public const string AuthenticationScheme = "UpstreamGateway";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Headers.TryGetValue(HeaderName, out var value)
            && Guid.TryParse(value.ToString(), out var userId)
            && userId != Guid.Empty)
        {
            var identity = new ClaimsIdentity(
                claims: new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
                authenticationType: AuthenticationScheme);
            context.User = new ClaimsPrincipal(identity);
        }

        return next(context);
    }
}
