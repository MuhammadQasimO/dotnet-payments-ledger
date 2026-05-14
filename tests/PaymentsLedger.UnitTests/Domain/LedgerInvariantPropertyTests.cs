using CsCheck;

using Microsoft.Extensions.Logging.Abstractions;

using PaymentsLedger.Application.Transfers;
using PaymentsLedger.Domain.Primitives;
using PaymentsLedger.Domain.Wallets;

namespace PaymentsLedger.UnitTests.Domain;

/// <summary>
/// Property-based tests for the central double-entry invariant: across ANY random
/// sequence of valid transfers, the sum of ledger entries per currency is always zero.
/// CsCheck generates the sequences; failures shrink to the minimal reproducing case.
/// </summary>
public sealed class LedgerInvariantPropertyTests
{
    private static readonly string[] _currencies = { "USD", "EUR", "GBP", "JPY" };

    private sealed record TransferStep(string Currency, int FromIdx, int ToIdx, long Amount);

    private static readonly Gen<TransferStep> _genStep =
        Gen.Select(
            Gen.OneOfConst(_currencies),
            Gen.Int[0, 5],
            Gen.Int[0, 5],
            Gen.Long[1L, 1_000_000L])
        .Select(t => new TransferStep(t.Item1, t.Item2, t.Item3, t.Item4));

    [Fact]
    public void Sum_of_entries_per_currency_is_always_zero_after_any_sequence_of_transfers()
    {
        _genStep.Array[0, 50].Sample(steps =>
        {
            var txRepo = new InMemoryTransactionRepository();
            var wallets = new InMemoryWalletRepository();
            var clock = new FixedClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var handler = new TransferHandler(
                wallets, txRepo, new NoopOutbox(), new NoopUnitOfWork(), clock,
                NullLogger<TransferHandler>.Instance);

            var walletsByCurrency = new Dictionary<string, List<Wallet>>(StringComparer.Ordinal);
            foreach (var ccy in _currencies)
            {
                var list = new List<Wallet>();
                for (var i = 0; i < 6; i++)
                {
                    var w = Wallet.Create(Guid.NewGuid(), ccy, clock.UtcNow);
                    wallets.Seed(w);
                    list.Add(w);
                }
                walletsByCurrency[ccy] = list;
            }

            var keyCounter = 0;
            foreach (var step in steps)
            {
                if (step.FromIdx == step.ToIdx)
                {
                    continue;
                }
                var pool = walletsByCurrency[step.Currency];
                var cmd = new TransferCommand(
                    IdempotencyKey: $"k-{keyCounter++}",
                    FromWalletId: pool[step.FromIdx].Id,
                    ToWalletId: pool[step.ToIdx].Id,
                    AmountMinorUnits: step.Amount,
                    Currency: step.Currency,
                    Reference: null);

                _ = handler.HandleAsync(cmd, CancellationToken.None).GetAwaiter().GetResult();
                clock.Advance(TimeSpan.FromMilliseconds(1));
            }

            // Invariant 1: per currency, every ledger entry sums to zero.
            var perCurrency = txRepo.AllEntries
                .GroupBy(e => e.Currency, StringComparer.Ordinal)
                .Select(g => g.Sum(e => e.Amount.MinorUnits));
            if (perCurrency.Any(s => s != 0))
            {
                return false;
            }

            // Invariant 2: no zero-amount entries are ever written.
            if (txRepo.AllEntries.Any(e => e.Amount.MinorUnits == 0))
            {
                return false;
            }

            // Invariant 3: per transaction, entries balance (mirrors the DB trigger).
            foreach (var grp in txRepo.AllEntries.GroupBy(e => e.TransactionId))
            {
                if (grp.GroupBy(e => e.Currency, StringComparer.Ordinal)
                       .Any(c => c.Sum(e => e.Amount.MinorUnits) != 0))
                {
                    return false;
                }
            }

            return true;
        }, iter: 200);
    }

    [Fact]
    public void Money_addition_is_associative_per_currency()
    {
        var genMoney = Gen.Long[-1_000_000L, 1_000_000L].Select(m => new Money(m, "USD"));

        Gen.Select(genMoney, genMoney, genMoney)
            .Sample(t =>
            {
                var left = (t.Item1 + t.Item2) + t.Item3;
                var right = t.Item1 + (t.Item2 + t.Item3);
                return left == right;
            }, iter: 500);
    }

    [Fact]
    public void Money_divide_quotient_times_divisor_plus_remainder_recovers_original()
    {
        Gen.Select(Gen.Long[-100_000L, 100_000L], Gen.Int[1, 97])
            .Sample(t =>
            {
                var money = new Money(t.Item1, "USD");
                var (q, r) = money.Divide(t.Item2);
                return q * t.Item2 + r == money;
            }, iter: 500);
    }
}
