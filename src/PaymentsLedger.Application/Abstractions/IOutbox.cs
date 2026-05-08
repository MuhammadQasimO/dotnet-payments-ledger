namespace PaymentsLedger.Application.Abstractions;

/// <summary>
/// Append-only outbox enqueued in the same database transaction as the entity write,
/// so messages are atomically committed with the state they describe.
/// </summary>
public interface IOutbox
{
    Task EnqueueAsync(
        string eventType,
        string aggregateType,
        Guid aggregateId,
        string payloadJson,
        CancellationToken cancellationToken);
}
