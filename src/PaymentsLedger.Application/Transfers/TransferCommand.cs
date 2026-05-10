using PaymentsLedger.Domain.Ledger;

namespace PaymentsLedger.Application.Transfers;

public sealed record TransferCommand(
    string IdempotencyKey,
    Guid FromWalletId,
    Guid ToWalletId,
    long AmountMinorUnits,
    string Currency,
    string? Reference);

public sealed record TransferResult(Guid TransactionId, TransactionStatus Status, IReadOnlyList<TransferEntry> Entries);

public sealed record TransferEntry(Guid LedgerEntryId, Guid WalletId, long AmountMinorUnits, string Currency);
