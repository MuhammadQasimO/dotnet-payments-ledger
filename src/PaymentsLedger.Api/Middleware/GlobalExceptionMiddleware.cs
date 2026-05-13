using System.Text.Json;

using Microsoft.AspNetCore.Mvc;

using PaymentsLedger.Domain.Exceptions;
using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.Api.Middleware;

/// <summary>
/// Translates uncaught exceptions into RFC 7807 <see cref="ProblemDetails"/> responses.
/// Keeps controllers free of try/catch noise; mapping rules live in one place.
/// </summary>
public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await WriteProblemAsync(context, ex);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception ex)
    {
        var (status, title, type) = Classify(ex);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(ex, "Unhandled exception: {Type}", ex.GetType().Name);
        }
        else
        {
            logger.LogWarning(ex, "Request failed with {Status}: {Type}", status, ex.GetType().Name);
        }

        if (context.Response.HasStarted)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = type,
            Detail = ex.Message,
            Instance = context.Request.Path,
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, _json));
    }

    private static (int Status, string Title, string Type) Classify(Exception ex) => ex switch
    {
        WalletNotFoundException =>
            (StatusCodes.Status404NotFound, "Wallet not found", "about:blank"),
        IdempotencyConflictException =>
            (StatusCodes.Status409Conflict, "Idempotency conflict", "about:blank"),
        UnbalancedTransactionException =>
            // Programmer/data error — bubble as 500 so it pages operators.
            (StatusCodes.Status500InternalServerError, "Unbalanced transaction", "about:blank"),
        CurrencyMismatchException =>
            (StatusCodes.Status400BadRequest, "Currency mismatch", "about:blank"),
        InvalidAmountException =>
            (StatusCodes.Status400BadRequest, "Invalid amount", "about:blank"),
        DomainException =>
            (StatusCodes.Status400BadRequest, "Domain rule violation", "about:blank"),
        ArgumentException =>
            (StatusCodes.Status400BadRequest, "Invalid argument", "about:blank"),
        _ =>
            (StatusCodes.Status500InternalServerError, "Internal server error", "about:blank"),
    };
}
