using PaymentsLedger.Application.Transfers;
using PaymentsLedger.Domain.Ledger;

namespace PaymentsLedger.Application.Transactions;

public sealed record GetTransactionQuery(Guid TransactionId);

public sealed record GetTransactionResult(
    Guid TransactionId,
    string IdempotencyKey,
    DateTimeOffset CreatedAt,
    TransactionStatus Status,
    IReadOnlyList<TransferEntry> Entries);
