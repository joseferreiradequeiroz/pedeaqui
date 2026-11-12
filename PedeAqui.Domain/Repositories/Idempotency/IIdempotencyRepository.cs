using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Idempotency;

public interface IIdempotencyRepository
{
    Task<IdempotencyRequest?> GetByKey(long userId, string key, CancellationToken cancellationToken);

    Task<bool> InsertIfKeyIsNew(IdempotencyRequest request, CancellationToken cancellationToken);

    Task<bool> TryTakeOverExpiredLock(long requestId, DateTime now, DateTime lockedUntil, CancellationToken cancellationToken);

    Task MarkCompleted(long requestId, int responseStatusCode, string? responseBody, DateTime completedAt, CancellationToken cancellationToken);

    Task DeleteInProgress(long requestId, CancellationToken cancellationToken);

    Task<bool> DeleteIfExpired(long requestId, DateTime now, CancellationToken cancellationToken);

    Task<int> DeleteExpired(DateTime now, CancellationToken cancellationToken);
}
