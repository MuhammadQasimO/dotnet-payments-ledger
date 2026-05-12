namespace PaymentsLedger.Infrastructure.Idempotency;

/// <summary>
/// Stripe-style idempotency store. Keyed by the client-provided <c>Idempotency-Key</c>
/// header; tracks the SHA-256 of the original request body so a second request with the
/// same key but a different body returns 409 instead of silently deduping.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Reads any prior cached record for <paramref name="key"/>.
    /// </summary>
    Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the final response so a future replay with the same body returns it.
    /// </summary>
    Task PutAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken cancellationToken);
}

public sealed record IdempotencyRecord(
    string RequestHash,
    int StatusCode,
    string? ContentType,
    string ResponseBody);
