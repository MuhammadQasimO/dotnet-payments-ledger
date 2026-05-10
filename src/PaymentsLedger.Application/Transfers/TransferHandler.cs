using System.Text.Json;

using Microsoft.Extensions.Logging;

using PaymentsLedger.Application.Abstractions;
using PaymentsLedger.Domain.Common;
using PaymentsLedger.Domain.Exceptions;
using PaymentsLedger.Domain.Ledger;
using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.Application.Transfers;

public sealed class TransferHandler(
    IWalletRepository wallets,
    ITransactionRepository transactions,
    IOutbox outbox,
    IUnitOfWork uow,
    IClock clock,
    ILogger<TransferHandler> logger)
{
    private static readonly JsonSerializerOptions _payloadOptions = new(JsonSerializerDefaults.Web);

    public async Task<TransferResult> HandleAsync(
        TransferCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Idempotency at the domain level: if a transaction with this key already exists,
        // return its existing entries instead of double-posting. The HTTP idempotency
        // middleware handles request-body replay separately at the edge.
        var existing = await transactions.FindByIdempotencyKeyAsync(
            command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation(
                "Transfer with idempotency key {Key} already exists as {TxId}; returning prior result.",
                command.IdempotencyKey, existing.Id);
            return ToResult(existing);
        }

        var from = await wallets.FindAsync(command.FromWalletId, cancellationToken)
            ?? throw new WalletNotFoundException(command.FromWalletId);
        var to = await wallets.FindAsync(command.ToWalletId, cancellationToken)
            ?? throw new WalletNotFoundException(command.ToWalletId);

        var amount = new Money(command.AmountMinorUnits, command.Currency);
        var tx = Transaction.NewTransfer(command.IdempotencyKey, from, to, amount, clock.UtcNow);

        await transactions.AddAsync(tx, cancellationToken);

        var payload = JsonSerializer.Serialize(new
        {
            transactionId = tx.Id,
            fromWalletId = from.Id,
            toWalletId = to.Id,
            amountMinorUnits = amount.MinorUnits,
            currency = amount.Currency,
            reference = command.Reference,
            occurredAt = tx.CreatedAt,
        }, _payloadOptions);

        await outbox.EnqueueAsync(
            eventType: "transfer.posted",
            aggregateType: "transaction",
            aggregateId: tx.Id,
            payloadJson: payload,
            cancellationToken);

        await uow.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Posted transfer {TxId}: {Amount} from {From} to {To}",
            tx.Id, amount.Format(), from.Id, to.Id);

        return ToResult(tx);
    }

    private static TransferResult ToResult(Transaction tx) =>
        new(
            tx.Id,
            tx.Status,
            tx.Entries
                .Select(e => new TransferEntry(e.Id, e.WalletId, e.Amount.MinorUnits, e.Currency))
                .ToList());
}
