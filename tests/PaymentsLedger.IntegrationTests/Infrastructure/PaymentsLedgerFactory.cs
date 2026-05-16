using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PaymentsLedger.Infrastructure.Outbox;
using PaymentsLedger.Infrastructure.Persistence;

namespace PaymentsLedger.IntegrationTests.Infrastructure;

/// <summary>
/// WebApplicationFactory for the Minimal-API host. Connection strings and other
/// settings are passed in via process env vars (see <see cref="LedgerFixture"/>) so they
/// land in <c>builder.Configuration</c> before <c>AddInfrastructure</c> reads them —
/// <c>ConfigureAppConfiguration</c> runs too late for that here.
/// Also strips the <see cref="OutboxDispatcher"/> hosted service (covered by dedicated tests).
/// </summary>
public sealed class PaymentsLedgerFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Test");

        builder.ConfigureServices(services =>
        {
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
