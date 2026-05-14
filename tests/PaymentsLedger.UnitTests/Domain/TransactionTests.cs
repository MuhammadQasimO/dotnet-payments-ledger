using PaymentsLedger.Domain.Exceptions;
using PaymentsLedger.Domain.Ledger;
using PaymentsLedger.Domain.Primitives;
using PaymentsLedger.Domain.Wallets;

namespace PaymentsLedger.UnitTests.Domain;

public sealed class TransactionTests
{
    private static readonly DateTimeOffset _now = new(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);

    private static Wallet WalletIn(string currency) =>
        Wallet.Create(Guid.NewGuid(), currency, _now);

    [Fact]
    public void NewTransfer_produces_two_balanced_entries()
    {
        var from = WalletIn("USD");
        var to = WalletIn("USD");

        var tx = Transaction.NewTransfer("key-1", from, to, new Money(1500, "USD"), _now);

        tx.Entries.Should().HaveCount(2);
        tx.Entries.Sum(e => e.Amount.MinorUnits).Should().Be(0);
        tx.Entries.Single(e => e.WalletId == from.Id).Amount.Should().Be(new Money(-1500, "USD"));
        tx.Entries.Single(e => e.WalletId == to.Id).Amount.Should().Be(new Money(1500, "USD"));
        tx.Status.Should().Be(TransactionStatus.Posted);
        tx.IdempotencyKey.Should().Be("key-1");
        tx.CreatedAt.Should().Be(_now);
    }

    [Fact]
    public void NewTransfer_rejects_zero_amount()
    {
        var act = () => Transaction.NewTransfer(
            "key-1", WalletIn("USD"), WalletIn("USD"), Money.Zero("USD"), _now);
        act.Should().Throw<InvalidAmountException>();
    }

    [Fact]
    public void NewTransfer_rejects_negative_amount()
    {
        var act = () => Transaction.NewTransfer(
            "key-1", WalletIn("USD"), WalletIn("USD"), new Money(-100, "USD"), _now);
        act.Should().Throw<InvalidAmountException>();
    }

    [Fact]
    public void NewTransfer_rejects_currency_mismatch_amount_vs_from_wallet()
    {
        var act = () => Transaction.NewTransfer(
            "key-1", WalletIn("USD"), WalletIn("USD"), new Money(100, "EUR"), _now);
        act.Should().Throw<CurrencyMismatchException>();
    }

    [Fact]
    public void NewTransfer_rejects_cross_currency_wallets()
    {
        var act = () => Transaction.NewTransfer(
            "key-1", WalletIn("USD"), WalletIn("EUR"), new Money(100, "USD"), _now);
        act.Should().Throw<CurrencyMismatchException>();
    }

    [Fact]
    public void NewTransfer_rejects_same_wallet()
    {
        var wallet = WalletIn("USD");
        var act = () => Transaction.NewTransfer(
            "key-1", wallet, wallet, new Money(100, "USD"), _now);
        act.Should().Throw<InvalidAmountException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NewTransfer_rejects_missing_idempotency_key(string? key)
    {
        var act = () => Transaction.NewTransfer(
            key!, WalletIn("USD"), WalletIn("USD"), new Money(100, "USD"), _now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EnsureBalanced_throws_when_entries_do_not_sum_to_zero()
    {
        // Force-construct an unbalanced transaction via Hydrate
        var txId = Guid.NewGuid();
        var tx = Transaction.Hydrate(
            txId,
            "key-1",
            _now,
            TransactionStatus.Posted,
            new[]
            {
                LedgerEntry.Hydrate(Guid.NewGuid(), txId, Guid.NewGuid(), new Money(-100, "USD"), _now),
                LedgerEntry.Hydrate(Guid.NewGuid(), txId, Guid.NewGuid(), new Money(50, "USD"), _now),
            });
        var act = () => tx.EnsureBalanced();
        act.Should().Throw<UnbalancedTransactionException>();
    }
}

public sealed class LedgerEntryTests
{
    [Fact]
    public void Create_rejects_zero_amount()
    {
        var act = () => typeof(LedgerEntry)
            .GetMethod("Create", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { Guid.NewGuid(), Guid.NewGuid(), Money.Zero("USD"), DateTimeOffset.UtcNow });
        act.Should().Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<ArgumentException>();
    }
}

public sealed class WalletTests
{
    [Fact]
    public void Create_rejects_empty_user_id()
    {
        var act = () => Wallet.Create(Guid.Empty, "USD", DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_invalid_currency()
    {
        var act = () => Wallet.Create(Guid.NewGuid(), "ZZZ", DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentException>();
    }
}
