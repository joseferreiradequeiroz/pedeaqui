using Microsoft.Extensions.Options;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Domain.Repositories.Idempotency;
using PedeAqui.Domain.Services;

namespace PedeAqui.Application.Idempotency;

public class IdempotencyService : IIdempotencyService
{
    private readonly IIdempotencyRepository _repository;
    private readonly IIdempotencyLock _lock;
    private readonly TimeSpan _keyTtl;
    private readonly TimeSpan _inProgressLockDuration;
    private string? _heldRedisLockKey;

    public IdempotencyService(IIdempotencyRepository repository, IIdempotencyLock idempotencyLock, IOptions<IdempotencyOptions> options)
    {
        _repository = repository;
        _lock = idempotencyLock;
        _keyTtl = TimeSpan.FromHours(options.Value.KeyTtlHours);
        _inProgressLockDuration = TimeSpan.FromSeconds(options.Value.InProgressLockSeconds);
    }

    public async Task<IdempotencyStart> Begin(long userId, string key, string method, string path, string requestBody, CancellationToken cancellationToken)
    {
        var requestHash = IdempotencyRequestHash.Of(requestBody);
        var redisLockKey = RedisLockKey(userId, key);

        var redisLockAcquired = await _lock.TryAcquire(redisLockKey, _inProgressLockDuration, cancellationToken);
        if (redisLockAcquired == false)
            return await ReplayIfAlreadyCompleted(userId, key, method, path, requestHash, cancellationToken);

        if (redisLockAcquired == true)
            _heldRedisLockKey = redisLockKey;

        IdempotencyStart start;

        try
        {
            start = await BeginInDatabase(userId, key, method, path, requestBody, requestHash, cancellationToken);
        }
        catch
        {
            await ReleaseRedisLock();
            throw;
        }

        if (start is not IdempotencyStart.Started)
            await ReleaseRedisLock();

        return start;
    }

    public async Task Complete(long requestId, int statusCode, string? body, CancellationToken cancellationToken)
    {
        try
        {
            await _repository.MarkCompleted(requestId, statusCode, body, DateTime.UtcNow, cancellationToken);
        }
        finally
        {
            await ReleaseRedisLock();
        }
    }

    public async Task Release(long requestId, CancellationToken cancellationToken)
    {
        try
        {
            await _repository.DeleteInProgress(requestId, cancellationToken);
        }
        finally
        {
            await ReleaseRedisLock();
        }
    }

    private async Task<IdempotencyStart> ReplayIfAlreadyCompleted(
        long userId, string key, string method, string path, string requestHash, CancellationToken cancellationToken)
    {
        var existing = await GetUnexpiredByKey(userId, key, cancellationToken);

        if (existing is { Status: IdempotencyStatus.Completed })
            return await ContinueFrom(existing, method, path, requestHash, cancellationToken);

        return new IdempotencyStart.InProgressConflict();
    }

    private async Task<IdempotencyStart> BeginInDatabase(
        long userId, string key, string method, string path, string requestBody, string requestHash, CancellationToken cancellationToken)
    {
        var existing = await GetUnexpiredByKey(userId, key, cancellationToken, discardExpired: true);
        if (existing is not null)
            return await ContinueFrom(existing, method, path, requestHash, cancellationToken);

        var now = DateTime.UtcNow;
        var request = new IdempotencyRequest
        {
            UserId = userId,
            Key = key,
            Method = method,
            Path = path,
            RequestBody = requestBody,
            RequestHash = requestHash,
            Status = IdempotencyStatus.InProgress,
            CreatedAt = now,
            ExpiresAt = now.Add(_keyTtl),
            LockedUntil = now.Add(_inProgressLockDuration)
        };

        if (await _repository.InsertIfKeyIsNew(request, cancellationToken))
            return new IdempotencyStart.Started(request.Id);

        var insertedByConcurrentRequest = await GetUnexpiredByKey(userId, key, cancellationToken);
        if (insertedByConcurrentRequest is null)
            return new IdempotencyStart.InProgressConflict();

        return await ContinueFrom(insertedByConcurrentRequest, method, path, requestHash, cancellationToken);
    }

    private async Task<IdempotencyStart> ContinueFrom(IdempotencyRequest existing, string method, string path, string requestHash, CancellationToken cancellationToken)
    {
        if (!existing.IsSameOperationAs(method, path, requestHash))
            return new IdempotencyStart.PayloadMismatch();

        if (existing.Status == IdempotencyStatus.Completed)
            return new IdempotencyStart.Replay(existing.ResponseStatusCode!.Value, existing.ResponseBody);

        var now = DateTime.UtcNow;
        if (existing.IsLockedAt(now))
            return new IdempotencyStart.InProgressConflict();

        var tookOver = await _repository.TryTakeOverExpiredLock(existing.Id, now, now.Add(_inProgressLockDuration), cancellationToken);

        return tookOver ? new IdempotencyStart.Started(existing.Id) : new IdempotencyStart.InProgressConflict();
    }

    private async Task<IdempotencyRequest?> GetUnexpiredByKey(long userId, string key, CancellationToken cancellationToken, bool discardExpired = false)
    {
        var existing = await _repository.GetByKey(userId, key, cancellationToken);
        if (existing is null)
            return null;

        var now = DateTime.UtcNow;
        if (!existing.IsExpiredAt(now))
            return existing;

        if (discardExpired)
            await _repository.DeleteIfExpired(existing.Id, now, cancellationToken);

        return null;
    }

    private async Task ReleaseRedisLock()
    {
        if (_heldRedisLockKey is null)
            return;

        var lockKey = _heldRedisLockKey;
        _heldRedisLockKey = null;
        await _lock.Release(lockKey);
    }

    private static string RedisLockKey(long userId, string key) => $"idem:{userId}:{key}";
}
