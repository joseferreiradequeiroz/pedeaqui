using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Domain.Repositories.Idempotency;

namespace PedeAqui.UnitTests.Fakes;

public sealed class InMemoryIdempotencyRepository : IIdempotencyRepository
{
    private readonly List<IdempotencyRequest> _requests = [];
    private long _nextId = 1;

    public InMemoryIdempotencyRepository(params IdempotencyRequest[] requests)
    {
        foreach (var request in requests)
            Store(request);
    }

    public IReadOnlyList<IdempotencyRequest> Requests => _requests;
    public IdempotencyRequest? InsertedByConcurrentRequestBeforeOurs { get; set; }
    public int ReadCount { get; private set; }

    public System.Exception? ReadFailure { get; set; }

    public Task<IdempotencyRequest?> GetByKey(long userId, string key, CancellationToken cancellationToken)
    {
        ReadCount++;

        if (ReadFailure is not null)
            throw ReadFailure;

        return Task.FromResult(_requests.FirstOrDefault(request => request.UserId == userId && request.Key == key));
    }

    public Task<bool> InsertIfKeyIsNew(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        if (InsertedByConcurrentRequestBeforeOurs is { } winner)
        {
            Store(winner);
            InsertedByConcurrentRequestBeforeOurs = null;
        }

        if (_requests.Any(stored => stored.UserId == request.UserId && stored.Key == request.Key))
            return Task.FromResult(false);

        Store(request);
        return Task.FromResult(true);
    }

    public Task<bool> TryTakeOverExpiredLock(long requestId, DateTime now, DateTime lockedUntil, CancellationToken cancellationToken)
    {
        var request = _requests.FirstOrDefault(stored =>
            stored.Id == requestId && stored.Status == IdempotencyStatus.InProgress && stored.LockedUntil <= now);

        if (request is null)
            return Task.FromResult(false);

        request.LockedUntil = lockedUntil;
        return Task.FromResult(true);
    }

    public Task MarkCompleted(long requestId, int responseStatusCode, string? responseBody, DateTime completedAt, CancellationToken cancellationToken)
    {
        var request = _requests.Single(stored => stored.Id == requestId);
        request.Status = IdempotencyStatus.Completed;
        request.ResponseStatusCode = responseStatusCode;
        request.ResponseBody = responseBody;
        request.CompletedAt = completedAt;
        return Task.CompletedTask;
    }

    public Task DeleteInProgress(long requestId, CancellationToken cancellationToken)
    {
        _requests.RemoveAll(stored => stored.Id == requestId && stored.Status == IdempotencyStatus.InProgress);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteIfExpired(long requestId, DateTime now, CancellationToken cancellationToken)
    {
        var deleted = _requests.RemoveAll(stored => stored.Id == requestId && stored.ExpiresAt <= now);
        return Task.FromResult(deleted == 1);
    }

    public Task<int> DeleteExpired(DateTime now, CancellationToken cancellationToken)
    {
        var deleted = _requests.RemoveAll(stored => stored.ExpiresAt <= now);
        return Task.FromResult(deleted);
    }

    private void Store(IdempotencyRequest request)
    {
        if (request.Id == 0)
            request.Id = _nextId;

        _nextId = Math.Max(_nextId, request.Id) + 1;
        _requests.Add(request);
    }
}
