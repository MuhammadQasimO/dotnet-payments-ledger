using PaymentsLedger.Domain.Exceptions;
using PaymentsLedger.Domain.Primitives;
using PaymentsLedger.Domain.Wallets;

namespace PaymentsLedger.Domain.Ledger;

/// <summary>
/// Aggregate root for a single atomic posting to the ledger. Contains two or more
/// <see cref="LedgerEntry"/> rows that MUST sum to zero per currency. The constraint is
/// enforced here for fail-fast feedback AND in the database via a deferred trigger;
/// the DB is the source of truth, the in-memory check is defence-in-depth.
/// </summary>
public sealed class Transaction
{
    private List<LedgerEntry> _entries = new();

    // EF Core constructor — entries are loaded separately by the repository.
    private Transaction() { IdempotencyKey = null!; }

    private Transaction(
        Guid id,
        string idempotencyKey,
        DateTimeOffset createdAt,
        TransactionStatus status,
        List<LedgerEntry> entries)
    {
        Id = id;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
        Status = status;
        _entries = entries;
    }

    public Guid Id { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public TransactionStatus Status { get; private set; }

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    public void AttachEntries(IEnumerable<LedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToList();
    }

    /// <summary>
    /// Constructs a same-currency transfer: a debit on <paramref name="fromWallet"/> and a
    /// credit on <paramref name="toWallet"/> of equal magnitude. Validates that the two
    /// wallets share a currency and that the amount is strictly positive.
    /// </summary>
    public static Transaction NewTransfer(
        string idempotencyKey,
        Wallet fromWallet,
        Wallet toWallet,
        Money amount,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(fromWallet);
        ArgumentNullException.ThrowIfNull(toWallet);

        if (!amount.IsPositive)
        {
            throw new InvalidAmountException(
                $"Transfer amount must be positive, got {amount.Format()}.");
        }
        if (!string.Equals(fromWallet.Currency, amount.Currency, StringComparison.Ordinal))
        {
            throw new CurrencyMismatchException(fromWallet.Currency, amount.Currency);
        }
        if (!string.Equals(toWallet.Currency, amount.Currency, StringComparison.Ordinal))
        {
            throw new CurrencyMismatchException(toWallet.Currency, amount.Currency);
        }
        if (fromWallet.Id == toWallet.Id)
        {
            throw new InvalidAmountException("Cannot transfer to the same wallet.");
        }

        var id = Guid.NewGuid();
        var entries = new List<LedgerEntry>(capacity: 2)
        {
            LedgerEntry.Create(id, fromWallet.Id, -amount, now),
            LedgerEntry.Create(id, toWallet.Id, amount, now),
        };

        var tx = new Transaction(id, idempotencyKey, now, TransactionStatus.Posted, entries);
        tx.EnsureBalanced();
        return tx;
    }

    /// <summary>
    /// Verifies the in-memory invariant: for every currency present in this transaction,
    /// the sum of entry amounts is zero. The database enforces the same rule via a
    /// deferred constraint trigger.
    /// </summary>
    public void EnsureBalanced()
    {
        var sums = _entries
            .GroupBy(e => e.Currency, StringComparer.Ordinal)
            .Select(g => (Currency: g.Key, Sum: g.Sum(e => e.Amount.MinorUnits)))
            .ToList();

        foreach (var (currency, sum) in sums)
        {
            if (sum != 0)
            {
                throw new UnbalancedTransactionException(
                    Id,
                    $"currency {currency} imbalance of {sum} minor units");
            }
        }
    }

    public static Transaction Hydrate(
        Guid id,
        string idempotencyKey,
        DateTimeOffset createdAt,
        TransactionStatus status,
        IEnumerable<LedgerEntry> entries) =>
        new(id, idempotencyKey, createdAt, status, entries.ToList());
}
