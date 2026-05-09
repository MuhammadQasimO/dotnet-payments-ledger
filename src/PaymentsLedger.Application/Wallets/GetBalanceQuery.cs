using PaymentsLedger.Domain.Primitives;

namespace PaymentsLedger.Application.Wallets;

public sealed record GetBalanceQuery(Guid WalletId, DateTimeOffset? AsOf);

public sealed record GetBalanceResult(Guid WalletId, Money Balance);
