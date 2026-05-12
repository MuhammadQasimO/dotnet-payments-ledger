using PaymentsLedger.Application.Abstractions;
using PaymentsLedger.Domain.Common;
using PaymentsLedger.Infrastructure.Persistence;

namespace PaymentsLedger.Infrastructure.Outbox;

internal sealed class EfOutbox(LedgerDbContext db, IClock clock) : IOutbox
{
    public async Task EnqueueAsync(
        string eventType,
        string aggregateType,
        Guid aggregateId,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var message = new OutboxMessage(
            id: Guid.NewGuid(),
            eventType: eventType,
            aggregateType: aggregateType,
            aggregateId: aggregateId,
            payloadJson: payloadJson,
            createdAt: now,
            nextAttemptAt: now);
        await db.OutboxMessages.AddAsync(message, cancellationToken);
    }
}
