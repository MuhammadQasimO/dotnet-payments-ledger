using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.UnitTests.MoneyTests;

public sealed class MoneyTests
{
    public sealed class Construction
    {
        [Theory]
        [InlineData(0, "USD")]
        [InlineData(1500, "USD")]
        [InlineData(-1500, "USD")]
        [InlineData(long.MaxValue, "JPY")]
        [InlineData(long.MinValue, "BHD")]
        public void Accepts_valid_minor_units_and_currency(long minor, string currency)
        {
            var money = new Money(minor, currency);
            money.MinorUnits.Should().Be(minor);
            money.Currency.Should().Be(currency);
        }

        [Theory]
        [InlineData("usd")]    // lowercase
        [InlineData("US")]     // too short
        [InlineData("USDD")]   // too long
        [InlineData("12A")]    // digits
        [InlineData(" USD")]   // whitespace
        [InlineData("")]       // empty
        public void Rejects_malformed_currency_codes(string bad)
        {
            var act = () => _ = new Money(100, bad);
            act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("currency");
        }

        [Fact]
        public void Rejects_unsupported_but_well_shaped_currency()
        {
            var act = () => _ = new Money(100, "ZZZ");
            act.Should()
                .Throw<ArgumentException>()
                .WithMessage("*not in the supported set*");
        }

        [Fact]
        public void Rejects_null_currency()
        {
            var act = () => _ = new Money(100, null!);
            act.Should().Throw<ArgumentNullException>();
        }
    }

    public sealed class Arithmetic
    {
        [Fact]
        public void Addition_same_currency_sums_minor_units()
        {
            var result = new Money(1500, "USD") + new Money(2500, "USD");
            result.Should().Be(new Money(4000, "USD"));
        }

        [Fact]
        public void Subtraction_can_go_negative()
        {
            var result = new Money(500, "USD") - new Money(1500, "USD");
            result.Should().Be(new Money(-1000, "USD"));
            result.IsNegative.Should().BeTrue();
        }

        [Fact]
        public void Addition_across_currencies_throws()
        {
            var act = () => _ = new Money(100, "USD") + new Money(100, "EUR");
            act.Should()
                .Throw<CurrencyMismatchException>()
                .Where(e => e.Left == "USD" && e.Right == "EUR");
        }

        [Fact]
        public void Subtraction_across_currencies_throws()
        {
            var act = () => _ = new Money(100, "USD") - new Money(100, "EUR");
            act.Should().Throw<CurrencyMismatchException>();
        }

        [Fact]
        public void Unary_negation_flips_sign()
        {
            (-new Money(750, "USD")).Should().Be(new Money(-750, "USD"));
            (-new Money(-750, "USD")).Should().Be(new Money(750, "USD"));
            (-Money.Zero("USD")).Should().Be(Money.Zero("USD"));
        }

        [Fact]
        public void Multiplication_by_long_scales_minor_units()
        {
            (new Money(150, "USD") * 7).Should().Be(new Money(1050, "USD"));
            (7 * new Money(150, "USD")).Should().Be(new Money(1050, "USD"));
            (new Money(150, "USD") * 0).Should().Be(Money.Zero("USD"));
            (new Money(150, "USD") * -1).Should().Be(new Money(-150, "USD"));
        }

        [Fact]
        public void Addition_overflow_throws_OverflowException()
        {
            var act = () => _ = new Money(long.MaxValue, "USD") + new Money(1, "USD");
            act.Should().Throw<OverflowException>();
        }

        [Fact]
        public void Multiplication_overflow_throws_OverflowException()
        {
            var act = () => _ = new Money(long.MaxValue / 2 + 1, "USD") * 2;
            act.Should().Throw<OverflowException>();
        }
    }

    public sealed class Comparison
    {
        [Fact]
        public void Comparisons_work_for_same_currency()
        {
            var a = new Money(100, "USD");
            var b = new Money(200, "USD");

            (a < b).Should().BeTrue();
            (b > a).Should().BeTrue();
            var aCopy = new Money(100, "USD");
            (a <= aCopy).Should().BeTrue();
            (a >= aCopy).Should().BeTrue();
            (a > b).Should().BeFalse();
        }

        [Fact]
        public void Comparison_across_currencies_throws()
        {
            var usd = new Money(100, "USD");
            var eur = new Money(100, "EUR");
            var act = () => _ = usd < eur;
            act.Should().Throw<CurrencyMismatchException>();
        }
    }

    public sealed class DivisionAndAllocation
    {
        [Fact]
        public void Divide_returns_quotient_and_remainder_no_silent_rounding()
        {
            var (q, r) = new Money(100, "USD").Divide(3);
            q.Should().Be(new Money(33, "USD"));
            r.Should().Be(new Money(1, "USD"));
            (q * 3 + r).Should().Be(new Money(100, "USD"));
        }

        [Fact]
        public void Divide_by_zero_throws()
        {
            var act = () => _ = new Money(100, "USD").Divide(0);
            act.Should().Throw<DivideByZeroException>();
        }

        [Fact]
        public void Divide_with_negative_value_preserves_sign()
        {
            var (q, r) = new Money(-100, "USD").Divide(3);
            (q * 3 + r).Should().Be(new Money(-100, "USD"));
        }

        [Fact]
        public void Allocate_distributes_remainder_to_first_shares()
        {
            var shares = new Money(100, "USD").Allocate(3);
            shares.Should().Equal(
                new Money(34, "USD"),
                new Money(33, "USD"),
                new Money(33, "USD"));

            shares.Aggregate(Money.Zero("USD"), (acc, m) => acc + m)
                .Should().Be(new Money(100, "USD"));
        }

        [Fact]
        public void Allocate_negative_amount_preserves_sign_in_distribution()
        {
            var shares = new Money(-100, "USD").Allocate(3);
            shares.Aggregate(Money.Zero("USD"), (acc, m) => acc + m)
                .Should().Be(new Money(-100, "USD"));
        }

        [Fact]
        public void Allocate_invalid_parts_throws()
        {
            var act = () => new Money(100, "USD").Allocate(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    public sealed class Formatting
    {
        [Theory]
        [InlineData(1234, "USD", "12.34 USD")]
        [InlineData(0, "USD", "0.00 USD")]
        [InlineData(5, "USD", "0.05 USD")]
        [InlineData(-1234, "USD", "-12.34 USD")]
        [InlineData(1000, "JPY", "1000 JPY")]
        [InlineData(-1000, "JPY", "-1000 JPY")]
        [InlineData(12345, "BHD", "12.345 BHD")]
        [InlineData(7, "BHD", "0.007 BHD")]
        public void Format_uses_currency_specific_minor_units(long minor, string ccy, string expected)
        {
            new Money(minor, ccy).Format().Should().Be(expected);
        }

        [Fact]
        public void ToString_matches_Format()
        {
            var money = new Money(1234, "USD");
            money.ToString().Should().Be(money.Format());
        }
    }

    public sealed class Equality
    {
        [Fact]
        public void Record_struct_equality_is_structural()
        {
            var a = new Money(100, "USD");
            var b = new Money(100, "USD");
            var c = new Money(100, "EUR");
            var d = new Money(101, "USD");

            a.Should().Be(b);
            (a == b).Should().BeTrue();
            (a != c).Should().BeTrue();
            (a != d).Should().BeTrue();
            a.GetHashCode().Should().Be(b.GetHashCode());
        }
    }

    public sealed class Predicates
    {
        [Fact]
        public void Sign_predicates_match_minor_units()
        {
            Money.Zero("USD").IsZero.Should().BeTrue();
            new Money(1, "USD").IsPositive.Should().BeTrue();
            new Money(-1, "USD").IsNegative.Should().BeTrue();
            new Money(-7, "USD").Abs().Should().Be(new Money(7, "USD"));
            new Money(7, "USD").Abs().Should().Be(new Money(7, "USD"));
        }
    }
}
