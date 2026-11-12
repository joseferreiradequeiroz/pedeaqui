namespace PedeAqui.Domain.Services;

public interface IIdempotencyLock
{
    Task<bool?> TryAcquire(string lockKey, TimeSpan ttl, CancellationToken cancellationToken);

    Task Release(string lockKey);
}
