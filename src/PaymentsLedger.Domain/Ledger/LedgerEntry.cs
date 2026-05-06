using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.Domain.Ledger;

/// <summary>
/// A single signed posting against a wallet. Immutable in domain semantics and append-only
/// in persistence (no UPDATE, no DELETE): corrections post a compensating entry in a new
/// transaction. This is what makes the ledger auditable.
/// </summary>
public sealed class LedgerEntry
{
    // EF Core constructor — Amount (owned) is materialised by EF, not via this ctor.
    private LedgerEntry() { Amount = default; }

    private LedgerEntry(
        Guid id,
        Guid transactionId,
        Guid walletId,
        Money amount,
        DateTimeOffset createdAt)
    {
        Id = id;
        TransactionId = transactionId;
        WalletId = walletId;
        Amount = amount;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid WalletId { get; private set; }
    public Money Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public string Currency => Amount.Currency;

    internal static LedgerEntry Create(Guid transactionId, Guid walletId, Money amount, DateTimeOffset now)
    {
        if (amount.IsZero)
        {
            throw new ArgumentException("Ledger entry amount must be non-zero.", nameof(amount));
        }
        return new LedgerEntry(Guid.NewGuid(), transactionId, walletId, amount, now);
    }

    public static LedgerEntry Hydrate(
        Guid id,
        Guid transactionId,
        Guid walletId,
        Money amount,
        DateTimeOffset createdAt) =>
        new(id, transactionId, walletId, amount, createdAt);
}
