using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PaymentsLedger.Infrastructure.Persistence;
using PaymentsLedger.IntegrationTests.Infrastructure;

namespace PaymentsLedger.IntegrationTests;

[Collection(LedgerCollectionFixture.Name)]
public sealed class TransfersFlowTests(LedgerFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task End_to_end_transfer_posts_balanced_entries_and_updates_balances()
    {
        var client = fixture.CreateClient(Guid.NewGuid());

        var (fromId, toId) = await CreateWalletsAsync(client, "USD");
        var transferResponse = await PostTransferAsync(client, "key-1", fromId, toId, 1500, "USD");

        transferResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await AssertBalanceAsync(client, fromId, -1500);
        await AssertBalanceAsync(client, toId, 1500);
    }

    [Fact]
    public async Task Replay_with_same_idempotency_key_and_body_returns_cached_response()
    {
        var client = fixture.CreateClient(Guid.NewGuid());
        var (fromId, toId) = await CreateWalletsAsync(client, "USD");

        var first = await PostTransferAsync(client, "replay-key", fromId, toId, 500, "USD");
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await PostTransferAsync(client, "replay-key", fromId, toId, 500, "USD");
        second.Headers.GetValues("Idempotent-Replay").Should().ContainSingle().Which.Should().Be("true");
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        // Balance should NOT have doubled.
        await AssertBalanceAsync(client, fromId, -500);
    }

    [Fact]
    public async Task Replay_with_same_idempotency_key_but_different_body_returns_409()
    {
        var client = fixture.CreateClient(Guid.NewGuid());
        var (fromId, toId) = await CreateWalletsAsync(client, "USD");

        var first = await PostTransferAsync(client, "conflict-key", fromId, toId, 1000, "USD");
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await PostTransferAsync(client, "conflict-key", fromId, toId, 2000, "USD");
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Missing_idempotency_key_on_transfer_returns_400()
    {
        var client = fixture.CreateClient(Guid.NewGuid());
        var (fromId, toId) = await CreateWalletsAsync(client, "USD");

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    fromWalletId = fromId,
                    toWalletId = toId,
                    amountMinorUnits = 100,
                    currency = "USD",
                    reference = (string?)null,
                }, _json),
                Encoding.UTF8,
                "application/json"),
        };
        var response = await client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cross_currency_transfer_returns_400()
    {
        var client = fixture.CreateClient(Guid.NewGuid());
        var usdId = await CreateWalletAsync(client, "USD");
        var eurId = await CreateWalletAsync(client, "EUR");

        var response = await PostTransferAsync(client, "xcur-1", usdId, eurId, 100, "USD");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deferred_trigger_rolls_back_an_unbalanced_raw_insert()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();

        // Bypass the application layer entirely and force two unequal entries within one DB
        // transaction. The deferred trigger should fire at COMMIT and roll the whole thing back.
        await using var tx = await db.Database.BeginTransactionAsync();
        var txId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO transactions (id, idempotency_key, created_at, status)
            VALUES ({txId}, {"raw-bad-" + txId}, NOW(), 1);");

        var walletA = Guid.NewGuid();
        var walletB = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO wallets (id, user_id, currency, created_at)
            VALUES ({walletA}, {Guid.NewGuid()}, 'USD', NOW()),
                   ({walletB}, {Guid.NewGuid()}, 'USD', NOW());");

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ledger_entries (id, transaction_id, wallet_id, created_at, currency, amount)
            VALUES ({Guid.NewGuid()}, {txId}, {walletA}, NOW(), 'USD', -100),
                   ({Guid.NewGuid()}, {txId}, {walletB}, NOW(), 'USD',   50);");

        var commit = async () => await tx.CommitAsync();
        await commit.Should()
            .ThrowAsync<Npgsql.PostgresException>()
            .Where(e => e.SqlState == "P0001");

        // Nothing should have made it through the rollback.
        var leftovers = await db.LedgerEntries.AsNoTracking().CountAsync(e => e.TransactionId == txId);
        leftovers.Should().Be(0);
    }

    [Fact]
    public async Task Outbox_row_is_written_alongside_transfer_in_the_same_transaction()
    {
        var client = fixture.CreateClient(Guid.NewGuid());
        var (fromId, toId) = await CreateWalletsAsync(client, "USD");

        var resp = await PostTransferAsync(client, "outbox-1", fromId, toId, 250, "USD");
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var rows = await db.OutboxMessages.AsNoTracking().Where(m => m.EventType == "transfer.posted").ToListAsync();
        rows.Should().NotBeEmpty();
        rows[0].PayloadJson.Should().Contain("\"amountMinorUnits\":250");
    }

    private static async Task<(Guid From, Guid To)> CreateWalletsAsync(HttpClient client, string currency) =>
        (await CreateWalletAsync(client, currency), await CreateWalletAsync(client, currency));

    private static async Task<Guid> CreateWalletAsync(HttpClient client, string currency)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/wallets",
            new { userId = Guid.NewGuid(), currency },
            _json);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(_json);
        return body.GetProperty("walletId").GetGuid();
    }

    private static async Task<HttpResponseMessage> PostTransferAsync(
        HttpClient client, string idempotencyKey, Guid from, Guid to, long minor, string currency)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    fromWalletId = from,
                    toWalletId = to,
                    amountMinorUnits = minor,
                    currency,
                    reference = (string?)null,
                }, _json),
                Encoding.UTF8,
                "application/json"),
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(req);
    }

    private static async Task AssertBalanceAsync(HttpClient client, Guid walletId, long expectedMinor)
    {
        var resp = await client.GetAsync(new Uri($"/api/wallets/{walletId}/balance", UriKind.Relative));
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(_json);
        body.GetProperty("balance").GetProperty("amountMinorUnits").GetInt64()
            .Should().Be(expectedMinor);
    }
}
