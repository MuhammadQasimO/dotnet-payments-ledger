using PaymentsLedger.Application.Abstractions;
using PaymentsLedger.Domain.Common;
using PaymentsLedger.Domain.Ledger;
using PaymentsLedger.Domain.Primitives;
using PaymentsLedger.Domain.Wallets;

namespace PaymentsLedger.UnitTests.Domain;

/// <summary>
/// In-memory wallet repo. Add returns void; tests use AddSynchronously to seed.
/// </summary>
internal sealed class InMemoryWalletRepository : IWalletRepository
{
    private readonly Dictionary<Guid, Wallet> _byId = new();

    public Task AddAsync(Wallet wallet, CancellationToken cancellationToken)
    {
        _byId[wallet.Id] = wallet;
        return Task.CompletedTask;
    }

    public Task<Wallet?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public void Seed(Wallet w) => _byId[w.Id] = w;
}

internal sealed class InMemoryTransactionRepository : ITransactionRepository
{
    private readonly Dictionary<Guid, Transaction> _byId = new();
    private readonly Dictionary<string, Guid> _byKey = new(StringComparer.Ordinal);

    public List<LedgerEntry> AllEntries { get; } = new();

    public Task AddAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        _byId[transaction.Id] = transaction;
        _byKey[transaction.IdempotencyKey] = transaction.Id;
        AllEntries.AddRange(transaction.Entries);
        return Task.CompletedTask;
    }

    public Task<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_byId.GetValueOrDefault(id));

    public Task<Transaction?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_byKey.TryGetValue(key, out var id) ? _byId[id] : null);
}

internal sealed class InMemoryLedgerEntryRepository(InMemoryTransactionRepository txRepo) : ILedgerEntryRepository
{
    public Task<Money> GetBalanceAsync(
        Guid walletId, string currency, DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        var sum = txRepo.AllEntries
            .Where(e => e.WalletId == walletId && (asOf is null || e.CreatedAt <= asOf))
            .Sum(e => e.Amount.MinorUnits);
        return Task.FromResult(new Money(sum, currency));
    }
}

internal sealed class NoopOutbox : IOutbox
{
    public Task EnqueueAsync(
        string eventType, string aggregateType, Guid aggregateId, string payloadJson,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class NoopUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = now;
    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
