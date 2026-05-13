using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.Api.Contracts;

/// <summary>
/// Wire representation of <see cref="Money"/>. Sends both raw minor units (for safe
/// downstream math) AND a pre-formatted display string (so clients don't reinvent ISO
/// 4217 exponents).
/// </summary>
public sealed record MoneyDto(long AmountMinorUnits, string Currency, string Display)
{
    public static MoneyDto From(Money money) =>
        new(money.MinorUnits, money.Currency, money.Format());
}
