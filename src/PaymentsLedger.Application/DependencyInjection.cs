using Microsoft.Extensions.DependencyInjection;

using PaymentsLedger.Application.Transactions;
using PaymentsLedger.Application.Transfers;
using PaymentsLedger.Application.Wallets;

namespace PaymentsLedger.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateWalletHandler>();
        services.AddScoped<GetBalanceHandler>();
        services.AddScoped<TransferHandler>();
        services.AddScoped<GetTransactionHandler>();
        return services;
    }
}
