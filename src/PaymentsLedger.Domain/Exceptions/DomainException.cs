namespace PaymentsLedger.Domain.Exceptions;

/// <summary>Base type for expected, recoverable domain errors that map to 4xx responses.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
    protected DomainException(string message, Exception inner) : base(message, inner) { }
}

public sealed class WalletNotFoundException(Guid walletId)
    : DomainException($"Wallet {walletId} was not found.")
{
    public Guid WalletId { get; } = walletId;
}

public sealed class InvalidAmountException(string message) : DomainException(message);

public sealed class IdempotencyConflictException(string idempotencyKey)
    : DomainException(
        $"Idempotency-Key '{idempotencyKey}' was previously used with a different request body.")
{
    public string IdempotencyKey { get; } = idempotencyKey;
}

public sealed class UnbalancedTransactionException(Guid transactionId, string detail)
    : DomainException($"Transaction {transactionId} is unbalanced: {detail}")
{
    public Guid TransactionId { get; } = transactionId;
}
