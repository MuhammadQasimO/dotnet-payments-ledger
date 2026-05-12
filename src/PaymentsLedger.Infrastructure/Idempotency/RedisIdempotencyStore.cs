using System.Text.Json;

using StackExchange.Redis;

namespace PaymentsLedger.Infrastructure.Idempotency;

internal sealed class RedisIdempotencyStore(IConnectionMultiplexer redis) : IIdempotencyStore
{
    private const string KeyPrefix = "idem:";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = redis.GetDatabase();
        var value = await db.StringGetAsync(KeyPrefix + key);
        if (value.IsNullOrEmpty)
        {
            return null;
        }
        return JsonSerializer.Deserialize<IdempotencyRecord>(value!, _json);
    }

    public async Task PutAsync(string key, IdempotencyRecord record, TimeSpan ttl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = redis.GetDatabase();
        var payload = JsonSerializer.Serialize(record, _json);
        await db.StringSetAsync(KeyPrefix + key, payload, ttl);
    }
}
