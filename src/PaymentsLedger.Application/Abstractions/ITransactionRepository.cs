using PaymentsLedger.Domain.Ledger;

namespace PaymentsLedger.Application.Abstractions;

public interface ITransactionRepository
{
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken);
    Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<Transaction?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken);
}
