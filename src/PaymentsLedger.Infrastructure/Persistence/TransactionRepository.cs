using Microsoft.EntityFrameworkCore;

using PaymentsLedger.Application.Abstractions;
using PaymentsLedger.Domain.Ledger;

namespace PaymentsLedger.Infrastructure.Persistence;

internal sealed class TransactionRepository(LedgerDbContext db) : ITransactionRepository
{
    public async Task AddAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await db.Transactions.AddAsync(transaction, cancellationToken);
        await db.LedgerEntries.AddRangeAsync(transaction.Entries, cancellationToken);
    }

    public async Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var tx = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tx is null)
        {
            return null;
        }

        var entries = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.TransactionId == id)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
        tx.AttachEntries(entries);
        return tx;
    }

    public async Task<Transaction?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken)
    {
        var tx = await db.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IdempotencyKey == key, cancellationToken);
        if (tx is null)
        {
            return null;
        }
        var entries = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.TransactionId == tx.Id)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
        tx.AttachEntries(entries);
        return tx;
    }
}
