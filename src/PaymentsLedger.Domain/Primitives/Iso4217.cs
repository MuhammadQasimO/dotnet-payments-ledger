using System.Collections.Frozen;

namespace PaymentsLedger.Domain.Primitives;

/// <summary>
/// Lookup of ISO 4217 currencies with their minor-unit exponents.
/// </summary>
/// <remarks>
/// Curated set of widely-traded currencies. Reject anything outside this set so we
/// fail fast on typos and unsupported instruments; a production system would source
/// this table from <see href="https://www.iso.org/iso-4217-currency-codes.html"/>
/// or a maintained NuGet package (e.g. NodaMoney). The 3-letter shape is enforced
/// even for codes not in this table so callers see the right validation message.
/// </remarks>
public static class Iso4217
{
    private static readonly FrozenDictionary<string, byte> MinorUnitsByCode =
        new Dictionary<string, byte>(StringComparer.Ordinal)
        {
            // Zero-decimal (the gotcha class)
            ["JPY"] = 0,
            ["KRW"] = 0,
            ["VND"] = 0,
            ["CLP"] = 0,
            ["XOF"] = 0,
            ["XAF"] = 0,
            ["RWF"] = 0,
            ["UGX"] = 0,
            // Two-decimal (the common case)
            ["USD"] = 2, ["EUR"] = 2, ["GBP"] = 2, ["CAD"] = 2, ["AUD"] = 2,
            ["NZD"] = 2, ["CHF"] = 2, ["CNY"] = 2, ["HKD"] = 2, ["SGD"] = 2,
            ["INR"] = 2, ["PKR"] = 2, ["AED"] = 2, ["SAR"] = 2, ["ZAR"] = 2,
            ["BRL"] = 2, ["MXN"] = 2, ["PLN"] = 2, ["CZK"] = 2, ["DKK"] = 2,
            ["NOK"] = 2, ["SEK"] = 2, ["TRY"] = 2, ["IDR"] = 2, ["PHP"] = 2,
            ["THB"] = 2, ["MYR"] = 2, ["EGP"] = 2, ["NGN"] = 2, ["KES"] = 2,
            // Three-decimal
            ["BHD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["OMR"] = 3, ["TND"] = 3,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Returns true if the code is a supported ISO 4217 currency.</summary>
    public static bool IsSupported(string code) =>
        code is not null && MinorUnitsByCode.ContainsKey(code);

    /// <summary>Number of minor units per major unit (e.g. 2 for USD/EUR, 0 for JPY).</summary>
    public static byte MinorUnits(string code) =>
        MinorUnitsByCode.TryGetValue(code, out var u)
            ? u
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unsupported currency");

    /// <summary>Validates the textual shape independently of whether the code is in the supported set.</summary>
    public static bool IsValidShape(string? code) =>
        code is { Length: 3 } &&
        char.IsAsciiLetterUpper(code[0]) &&
        char.IsAsciiLetterUpper(code[1]) &&
        char.IsAsciiLetterUpper(code[2]);
}
