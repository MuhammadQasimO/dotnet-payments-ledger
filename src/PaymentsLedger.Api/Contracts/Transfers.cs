using System.ComponentModel.DataAnnotations;

using PaymentsLedger.Domain.Ledger;

namespace PaymentsLedger.Api.Contracts;

public sealed record TransferRequest(
    [Required] Guid FromWalletId,
    [Required] Guid ToWalletId,
    [Range(1, long.MaxValue)] long AmountMinorUnits,
    [Required, StringLength(3, MinimumLength = 3)] string Currency,
    string? Reference);

public sealed record TransferResponse(
    Guid TransactionId,
    TransactionStatus Status,
    IReadOnlyList<TransferEntryDto> Entries);

public sealed record TransferEntryDto(Guid LedgerEntryId, Guid WalletId, MoneyDto Amount);

public sealed record TransactionResponse(
    Guid TransactionId,
    string IdempotencyKey,
    DateTimeOffset CreatedAt,
    TransactionStatus Status,
    IReadOnlyList<TransferEntryDto> Entries);
