using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.Domain.Wallets;

/// <summary>
/// A wallet is an identity for a stream of <see cref="Ledger.LedgerEntry"/> rows in a single
/// currency. Balance is computed from those entries — never stored — so it cannot drift out
/// of sync with the ledger.
/// </summary>
public sealed class Wallet
{
    // EF Core constructor — never call from application code, use Create or Hydrate.
    private Wallet() { Currency = null!; }

    private Wallet(Guid id, Guid userId, string currency, DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        Currency = currency;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Wallet Create(Guid userId, string currency, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }
        // Re-uses Money's currency validation to keep the rule in one place.
        _ = Money.Zero(currency);
        return new Wallet(Guid.NewGuid(), userId, currency, now);
    }

    public static Wallet Hydrate(Guid id, Guid userId, string currency, DateTimeOffset createdAt) =>
        new(id, userId, currency, createdAt);
}
