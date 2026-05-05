namespace PaymentsLedger.Domain.Primitives;

public sealed class CurrencyMismatchException : InvalidOperationException
{
    public CurrencyMismatchException(string left, string right)
        : base($"Currency mismatch: cannot combine {left} with {right}.")
    {
        Left = left;
        Right = right;
    }

    public string Left { get; }
    public string Right { get; }
}
