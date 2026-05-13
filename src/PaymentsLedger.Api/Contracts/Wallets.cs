using System.ComponentModel.DataAnnotations;

namespace PaymentsLedger.Api.Contracts;

public sealed record CreateWalletRequest(
    [Required] Guid UserId,
    [Required, StringLength(3, MinimumLength = 3)] string Currency);

public sealed record CreateWalletResponse(Guid WalletId, string Currency, MoneyDto Balance);

public sealed record BalanceResponse(Guid WalletId, MoneyDto Balance, DateTimeOffset? AsOf);
