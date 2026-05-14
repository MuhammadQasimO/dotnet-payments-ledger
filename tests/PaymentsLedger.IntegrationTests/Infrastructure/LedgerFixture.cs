using System.Net.Http.Headers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PaymentsLedger.Infrastructure.Persistence;

using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace PaymentsLedger.IntegrationTests.Infrastructure;

/// <summary>
/// One Postgres + one Redis container per collection (shared across test classes in the
/// collection). Each test seeds and tears down its own data via the helper methods.
/// </summary>
public sealed class LedgerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("ledger_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public PaymentsLedgerFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        Factory = new PaymentsLedgerFactory(
            _postgres.GetConnectionString(),
            _redis.GetConnectionString());

        // Force startup so AutoMigrate runs.
        _ = Factory.Server;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    public HttpClient CreateClient(Guid? userId = null)
    {
        var client = Factory.CreateClient();
        if (userId is { } id)
        {
            client.DefaultRequestHeaders.Add("X-User-Id", id.ToString());
        }
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task ResetAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE ledger_entries, transactions, outbox_messages, wallets RESTART IDENTITY CASCADE;");
    }
}

[CollectionDefinition(LedgerCollectionFixture.Name)]
public sealed class LedgerCollectionFixture : ICollectionFixture<LedgerFixture>
{
    public const string Name = "Ledger";
}
