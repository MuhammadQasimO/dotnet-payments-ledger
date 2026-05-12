namespace PaymentsLedger.Infrastructure.Outbox;

/// <summary>
/// Append-only durable outbox row. Written in the same transaction as the entity that
/// produced it; dispatched by <c>OutboxDispatcher</c> later.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        EventType = AggregateType = PayloadJson = null!;
    }

    public OutboxMessage(
        Guid id,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        string payloadJson,
        DateTimeOffset createdAt,
        DateTimeOffset nextAttemptAt)
    {
        Id = id;
        EventType = eventType;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        PayloadJson = payloadJson;
        CreatedAt = createdAt;
        NextAttemptAt = nextAttemptAt;
    }

    public Guid Id { get; private set; }
    public string EventType { get; private set; }
    public string AggregateType { get; private set; }
    public Guid AggregateId { get; private set; }
    public string PayloadJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public bool DeadLetter { get; private set; }
    public string? LastError { get; private set; }

    public void MarkAttempt(string? error, DateTimeOffset nextAttemptAt)
    {
        Attempts++;
        LastError = error;
        NextAttemptAt = nextAttemptAt;
    }

    public void MarkSent(DateTimeOffset sentAt)
    {
        SentAt = sentAt;
        LastError = null;
    }

    public void MarkDeadLetter(string error)
    {
        DeadLetter = true;
        LastError = error;
    }
}
