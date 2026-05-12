using Microsoft.EntityFrameworkCore;

using PaymentsLedger.Application.Abstractions;
using PaymentsLedger.Domain.Wallets;

namespace PaymentsLedger.Infrastructure.Persistence;

internal sealed class WalletRepository(LedgerDbContext db) : IWalletRepository
{
    public async Task AddAsync(Wallet wallet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        await db.Wallets.AddAsync(wallet, cancellationToken);
    }

    public async Task<Wallet?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
}
