namespace PaymentsLedger.Domain.Common;

/// <summary>Wall-clock abstraction so tests can fix time.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
