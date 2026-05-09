using PaymentsLedger.Application.Abstractions;
using PaymentsLedger.Domain.Exceptions;

namespace PaymentsLedger.Application.Wallets;

public sealed class GetBalanceHandler(
    IWalletRepository wallets,
    ILedgerEntryRepository ledger)
{
    public async Task<GetBalanceResult> HandleAsync(
        GetBalanceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var wallet = await wallets.FindAsync(query.WalletId, cancellationToken)
            ?? throw new WalletNotFoundException(query.WalletId);

        var balance = await ledger.GetBalanceAsync(
            wallet.Id, wallet.Currency, query.AsOf, cancellationToken);

        return new GetBalanceResult(wallet.Id, balance);
    }
}
