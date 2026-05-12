using Microsoft.EntityFrameworkCore;

using PaymentsLedger.Application.Abstractions;
using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.Infrastructure.Persistence;

internal sealed class LedgerEntryRepository(LedgerDbContext db) : ILedgerEntryRepository
{
    public async Task<Money> GetBalanceAsync(
        Guid walletId,
        string currency,
        DateTimeOffset? asOf,
        CancellationToken cancellationToken)
    {
        // Single aggregation query — relies on ix_ledger_entries_wallet_id_created_at.
        var query = db.LedgerEntries.AsNoTracking().Where(e => e.WalletId == walletId);
        if (asOf is { } cutoff)
        {
            query = query.Where(e => e.CreatedAt <= cutoff);
        }

        var total = await query.SumAsync(e => (long?)e.Amount.MinorUnits, cancellationToken) ?? 0L;
        return new Money(total, currency);
    }
}
