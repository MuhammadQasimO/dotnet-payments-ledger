using PaymentsLedger.Application.Abstractions;
using PaymentsLedger.Application.Transfers;

namespace PaymentsLedger.Application.Transactions;

public sealed class GetTransactionHandler(ITransactionRepository transactions)
{
    public async Task<GetTransactionResult?> HandleAsync(
        GetTransactionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tx = await transactions.FindAsync(query.TransactionId, cancellationToken);
        if (tx is null)
        {
            return null;
        }

        return new GetTransactionResult(
            tx.Id,
            tx.IdempotencyKey,
            tx.CreatedAt,
            tx.Status,
            tx.Entries
                .Select(e => new TransferEntry(e.Id, e.WalletId, e.Amount.MinorUnits, e.Currency))
                .ToList());
    }
}
