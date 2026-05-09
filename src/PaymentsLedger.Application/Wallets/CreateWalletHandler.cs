using PaymentsLedger.Application.Abstractions;
using PaymentsLedger.Domain.Common;
using PaymentsLedger.Domain.Primitives;
using PaymentsLedger.Domain.Wallets;

namespace PaymentsLedger.Application.Wallets;

public sealed class CreateWalletHandler(
    IWalletRepository wallets,
    IUnitOfWork uow,
    IClock clock)
{
    public async Task<CreateWalletResult> HandleAsync(
        CreateWalletCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var wallet = Wallet.Create(command.UserId, command.Currency, clock.UtcNow);
        await wallets.AddAsync(wallet, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return new CreateWalletResult(wallet.Id, wallet.Currency, Money.Zero(wallet.Currency));
    }
}
