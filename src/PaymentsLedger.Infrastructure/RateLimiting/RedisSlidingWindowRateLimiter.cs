using StackExchange.Redis;

namespace PaymentsLedger.Infrastructure.RateLimiting;

internal sealed class RedisSlidingWindowRateLimiter(IConnectionMultiplexer redis) : IRateLimiter
{
    // Atomic sliding-window via a sorted-set. Each call:
    //   1. Removes entries older than (now - window)
    //   2. Counts what's left
    //   3. If under limit, ZADDs the current call and returns allowed=1
    //   4. Sets a TTL on the bucket so cold buckets self-evict
    // One round-trip; atomic — no race between check and consume.
    private const string ScriptSource = @"
local key    = KEYS[1]
local now    = tonumber(ARGV[1])
local window = tonumber(ARGV[2])
local limit  = tonumber(ARGV[3])
local member = ARGV[4]

redis.call('ZREMRANGEBYSCORE', key, 0, now - window)
local count = redis.call('ZCARD', key)
local allowed = 0
if count < limit then
    redis.call('ZADD', key, now, member)
    count = count + 1
    allowed = 1
end
redis.call('PEXPIRE', key, window)

local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
local retry_after = 0
if allowed == 0 and oldest[2] then
    retry_after = (tonumber(oldest[2]) + window) - now
    if retry_after < 0 then retry_after = 0 end
end
return { allowed, count, retry_after }
";

    public async Task<RateLimitDecision> CheckAsync(
        string bucket,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var db = redis.GetDatabase();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var member = $"{now}:{Guid.NewGuid():N}";
        var windowMs = (long)window.TotalMilliseconds;

        var raw = await db.ScriptEvaluateAsync(
            ScriptSource,
            new RedisKey[] { "ratelimit:" + bucket },
            new RedisValue[] { now, windowMs, limit, member });

        var result = (RedisResult[])raw!;
        var allowed = (long)result[0] == 1;
        var count = (int)(long)result[1];
        var retryAfterMs = (long)result[2];

        return new RateLimitDecision(
            Allowed: allowed,
            Limit: limit,
            Remaining: Math.Max(0, limit - count),
            RetryAfter: TimeSpan.FromMilliseconds(retryAfterMs));
    }
}
