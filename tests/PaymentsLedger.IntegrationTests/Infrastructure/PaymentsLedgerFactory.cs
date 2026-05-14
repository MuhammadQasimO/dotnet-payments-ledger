using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PaymentsLedger.Infrastructure.Outbox;
using PaymentsLedger.Infrastructure.Persistence;

namespace PaymentsLedger.IntegrationTests.Infrastructure;

/// <summary>
/// WebApplicationFactory that points the API at Testcontainers-managed Postgres + Redis.
/// Also strips the OutboxDispatcher (it's not under test here; dedicated tests cover it).
/// </summary>
public sealed class PaymentsLedgerFactory(string postgresConnection, string redisConnection)
    : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Ledger"] = postgresConnection,
                ["ConnectionStrings:Redis"] = redisConnection,
                ["AutoMigrate"] = "true",
                ["Webhooks:Endpoint"] = null,
                ["RateLimit:PerIpPerSecond"] = "10000",
                ["RateLimit:PerUserPerSecond"] = "10000",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the OutboxDispatcher hosted service in tests — covered separately.
            var dispatcher = services.SingleOrDefault(s =>
                s.ImplementationType == typeof(OutboxDispatcher));
            if (dispatcher is not null)
            {
                services.Remove(dispatcher);
            }
        });

        return base.CreateHost(builder);
    }

    public async Task<LedgerDbContext> CreateDbContextAsync()
    {
        var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await db.Database.MigrateAsync();
        return db;
    }
}
