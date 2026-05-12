namespace PaymentsLedger.Infrastructure.RateLimiting;

public interface IRateLimiter
{
    /// <summary>
    /// Atomic check-and-consume against a sliding window. Returns the decision plus the
    /// data needed to populate <c>X-RateLimit-*</c> headers.
    /// </summary>
    Task<RateLimitDecision> CheckAsync(
        string bucket,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken);
}

public sealed record RateLimitDecision(bool Allowed, int Limit, int Remaining, TimeSpan RetryAfter);
