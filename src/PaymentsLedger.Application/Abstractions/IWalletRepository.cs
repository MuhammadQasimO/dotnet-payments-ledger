using PaymentsLedger.Domain.Wallets;

namespace PaymentsLedger.Application.Abstractions;

public interface IWalletRepository
{
    Task AddAsync(Wallet wallet, CancellationToken cancellationToken);
    Task<Wallet?> FindAsync(Guid id, CancellationToken cancellationToken);
}
