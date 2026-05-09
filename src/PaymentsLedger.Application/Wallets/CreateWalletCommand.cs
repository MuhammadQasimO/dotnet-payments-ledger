using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.Application.Wallets;

public sealed record CreateWalletCommand(Guid UserId, string Currency);

public sealed record CreateWalletResult(Guid WalletId, string Currency, Money Balance);
