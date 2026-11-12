using PedeAqui.Domain.Services;
using StackExchange.Redis;

namespace PedeAqui.Infrastructure.Redis;

internal class RedisFixedWindowRateLimiter : IRateLimiter
{
    private const string COUNT_AND_REMAINING_TIME_SCRIPT = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        local remainingMilliseconds = redis.call('PTTL', KEYS[1])
        return { count, remainingMilliseconds }
        """;

    private readonly RedisConnection _redis;

    public RedisFixedWindowRateLimiter(RedisConnection redis)
    {
        _redis = redis;
    }

    public async Task<RateLimitDecision> Acquire(string bucket, int limit, TimeSpan window, CancellationToken cancellationToken)
    {
        var outcome = await _redis.TryExecute(
            async (database, _) => (RedisResult[])(await database.ScriptEvaluateAsync(
                COUNT_AND_REMAINING_TIME_SCRIPT,
                [(RedisKey)bucket],
                [(RedisValue)(long)window.TotalMilliseconds]))!,
            cancellationToken);

        if (!outcome.Succeeded || outcome.Value is null)
            return new RateLimitDecision(Allowed: true, TimeSpan.Zero);

        var count = (long)outcome.Value[0];
        var remainingMilliseconds = (long)outcome.Value[1];

        if (count <= limit)
            return new RateLimitDecision(Allowed: true, TimeSpan.Zero);

        var retryAfter = remainingMilliseconds > 0 ? TimeSpan.FromMilliseconds(remainingMilliseconds) : window;

        return new RateLimitDecision(Allowed: false, retryAfter);
    }
}
