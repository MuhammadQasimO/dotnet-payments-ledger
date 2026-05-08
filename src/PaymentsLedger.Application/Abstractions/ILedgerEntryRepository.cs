using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.Application.Abstractions;

public interface ILedgerEntryRepository
{
    /// <summary>
    /// Computes a wallet's balance by summing its ledger entries, optionally as of a
    /// historical timestamp. Returns <c>Money.Zero(currency)</c> when no entries exist.
    /// </summary>
    Task<Money> GetBalanceAsync(
        Guid walletId,
        string currency,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken);
}
