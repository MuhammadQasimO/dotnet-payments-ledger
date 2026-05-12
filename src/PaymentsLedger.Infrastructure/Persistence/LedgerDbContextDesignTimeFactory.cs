using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PaymentsLedger.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> at design time (migrations add/update) so it can build a
/// <see cref="LedgerDbContext"/> without spinning up the full application host.
/// </summary>
public sealed class LedgerDbContextDesignTimeFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    public LedgerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=ledger_design;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new LedgerDbContext(options);
    }
}
