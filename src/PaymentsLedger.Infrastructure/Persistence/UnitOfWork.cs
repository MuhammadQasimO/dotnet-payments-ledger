using Microsoft.EntityFrameworkCore;

using PaymentsLedger.Application.Abstractions;

namespace PaymentsLedger.Infrastructure.Persistence;

/// <summary>
/// Wraps <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> in an explicit DB
/// transaction so the <c>DEFERRABLE INITIALLY DEFERRED</c> constraint trigger on
/// <c>ledger_entries</c> fires at COMMIT — not on each row INSERT.
/// </summary>
internal sealed class UnitOfWork(LedgerDbContext db) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        // If an outer transaction is already in scope (test fixtures, nested handlers),
        // honour it. Otherwise open a fresh one so the deferred trigger sees a commit.
        if (db.Database.CurrentTransaction is not null)
        {
            return await db.SaveChangesAsync(cancellationToken);
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var changes = await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return changes;
    }
}
