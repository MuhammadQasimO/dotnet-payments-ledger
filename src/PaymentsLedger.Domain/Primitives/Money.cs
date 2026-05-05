using System.Globalization;

namespace PaymentsLedger.Domain.Primitives;

/// <summary>
/// Money expressed as signed minor units (cents, paise, kobo) plus an ISO 4217 currency.
/// </summary>
/// <remarks>
/// <para>
/// The whole project hinges on this type. Floating-point is unsafe for money; even
/// <see cref="decimal"/> hides rounding choices behind the type. Storing the value as
/// <see cref="long"/> minor units forces every rounding/splitting decision to be
/// explicit at the call site, which is what real card networks and processors do.
/// </para>
/// <para>
/// Arithmetic across different currencies throws — there is no implicit FX rate; cross-
/// currency conversion is an explicit, audited operation that lives outside the type.
/// </para>
/// <para>
/// Implemented as a <c>readonly record struct</c>: value semantics, structural equality,
/// stack-allocated, no boxing in collections.
/// </para>
/// </remarks>
public readonly record struct Money
{
    public Money(long minorUnits, string currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        if (!Iso4217.IsValidShape(currency))
        {
            throw new ArgumentException(
                $"Currency '{currency}' is not a valid ISO 4217 code (expected 3 uppercase letters).",
                nameof(currency));
        }
        if (!Iso4217.IsSupported(currency))
        {
            throw new ArgumentException(
                $"Currency '{currency}' is not in the supported set. Extend Iso4217 to add it.",
                nameof(currency));
        }

        MinorUnits = minorUnits;
        Currency = currency;
    }

    public long MinorUnits { get; }
    public string Currency { get; }

    public static Money Zero(string currency) => new(0, currency);

    public bool IsZero => MinorUnits == 0;
    public bool IsNegative => MinorUnits < 0;
    public bool IsPositive => MinorUnits > 0;

    public Money Negate() => new(-MinorUnits, Currency);
    public Money Abs() => MinorUnits >= 0 ? this : Negate();

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(checked(left.MinorUnits + right.MinorUnits), left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(checked(left.MinorUnits - right.MinorUnits), left.Currency);
    }

    public static Money operator -(Money value) => value.Negate();

    public static Money operator *(Money value, long factor) =>
        new(checked(value.MinorUnits * factor), value.Currency);

    public static Money operator *(long factor, Money value) => value * factor;

    /// <summary>
    /// Integer division with explicit remainder. There is no <c>operator /</c> overload
    /// because silent rounding of money is a bug. Callers MUST decide what to do with
    /// the remainder.
    /// </summary>
    public (Money Quotient, Money Remainder) Divide(int divisor)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException("Cannot divide Money by zero.");
        }

        var quotient = Math.DivRem(MinorUnits, divisor, out var remainder);
        return (new Money(quotient, Currency), new Money(remainder, Currency));
    }

    /// <summary>
    /// Splits this amount into <paramref name="parts"/> shares, distributing any
    /// remainder one minor unit at a time to the first shares so the total is preserved
    /// exactly. Example: <c>$1.00 ÷ 3 → [$0.34, $0.33, $0.33]</c>.
    /// </summary>
    public Money[] Allocate(int parts)
    {
        if (parts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parts), parts, "Parts must be positive.");
        }

        var (quotient, remainder) = Divide(parts);
        var leftover = Math.Abs(remainder.MinorUnits);
        var sign = Math.Sign(MinorUnits);

        var shares = new Money[parts];
        for (var i = 0; i < parts; i++)
        {
            var extra = i < leftover ? sign : 0;
            shares[i] = new Money(quotient.MinorUnits + extra, Currency);
        }
        return shares;
    }

    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.MinorUnits < right.MinorUnits;
    }

    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.MinorUnits > right.MinorUnits;
    }

    public static bool operator <=(Money left, Money right) => !(left > right);
    public static bool operator >=(Money left, Money right) => !(left < right);

    /// <summary>
    /// Culture-invariant display formatting using the currency's ISO 4217 minor-unit
    /// exponent (e.g. JPY=0, USD=2, BHD=3). Format: <c>{amount} {CCY}</c>.
    /// </summary>
    public string Format()
    {
        var exponent = Iso4217.MinorUnits(Currency);
        if (exponent == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{MinorUnits} {Currency}");
        }

        var scale = (long)Math.Pow(10, exponent);
        var negative = MinorUnits < 0;
        var abs = negative ? -MinorUnits : MinorUnits;
        var major = abs / scale;
        var minor = abs % scale;
        var sign = negative ? "-" : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{major}.{minor.ToString(CultureInfo.InvariantCulture).PadLeft(exponent, '0')} {Currency}");
    }

    public override string ToString() => Format();

    private static void EnsureSameCurrency(in Money left, in Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
        {
            throw new CurrencyMismatchException(left.Currency, right.Currency);
        }
    }
}
