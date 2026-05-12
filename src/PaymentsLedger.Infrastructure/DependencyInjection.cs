using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PaymentsLedger.Application.Abstractions;
using PaymentsLedger.Domain.Common;
using PaymentsLedger.Infrastructure.Idempotency;
using PaymentsLedger.Infrastructure.Outbox;
using PaymentsLedger.Infrastructure.Persistence;
using PaymentsLedger.Infrastructure.RateLimiting;
using PaymentsLedger.Infrastructure.Webhooks;

using StackExchange.Redis;

namespace PaymentsLedger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var dbConnection = configuration.GetConnectionString("Ledger")
            ?? throw new InvalidOperationException(
                "Missing connection string 'ConnectionStrings:Ledger'.");

        var redisConnection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException(
                "Missing connection string 'ConnectionStrings:Redis'.");

        services.AddDbContext<LedgerDbContext>(opts =>
        {
            opts.UseNpgsql(dbConnection, npg => npg.MigrationsHistoryTable("__ef_migrations_history"));
            opts.UseSnakeCaseNamingConvention();
        });

        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(redisConnection));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ILedgerEntryRepository, LedgerEntryRepository>();
        services.AddScoped<IOutbox, EfOutbox>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddSingleton<IRateLimiter, RedisSlidingWindowRateLimiter>();

        services.Configure<WebhookOptions>(configuration.GetSection(WebhookOptions.SectionName));
        services.AddHttpClient(OutboxDispatcher.HttpClientName);
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}
