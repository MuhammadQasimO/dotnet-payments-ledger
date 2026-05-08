namespace PaymentsLedger.Application.Abstractions;

/// <summary>
/// Coordinates a single atomic write. The implementation opens a database transaction
/// so the deferred constraint trigger on <c>ledger_entries</c> fires at COMMIT.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
