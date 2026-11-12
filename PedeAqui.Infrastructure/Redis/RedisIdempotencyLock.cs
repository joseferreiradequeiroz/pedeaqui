using PedeAqui.Domain.Services;
using StackExchange.Redis;

namespace PedeAqui.Infrastructure.Redis;

internal class RedisIdempotencyLock : IIdempotencyLock
{
    private const string DELETE_ONLY_IF_OWNER_SCRIPT = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    private readonly RedisConnection _redis;
    private readonly Dictionary<string, string> _ownerTokensByLockKey = [];

    public RedisIdempotencyLock(RedisConnection redis)
    {
        _redis = redis;
    }

    public async Task<bool?> TryAcquire(string lockKey, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var ownerToken = Guid.NewGuid().ToString("N");

        var outcome = await _redis.TryExecute<bool>(
            (database, _) => database.StringSetAsync(lockKey, ownerToken, ttl, When.NotExists),
            cancellationToken);

        if (!outcome.Succeeded)
            return null;

        if (outcome.Value)
            _ownerTokensByLockKey[lockKey] = ownerToken;

        return outcome.Value;
    }

    public async Task Release(string lockKey)
    {
        if (!_ownerTokensByLockKey.Remove(lockKey, out var ownerToken))
            return;

        await _redis.TryExecute(
            (database, _) => database.ScriptEvaluateAsync(DELETE_ONLY_IF_OWNER_SCRIPT, [(RedisKey)lockKey], [(RedisValue)ownerToken]),
            CancellationToken.None);
    }
}
