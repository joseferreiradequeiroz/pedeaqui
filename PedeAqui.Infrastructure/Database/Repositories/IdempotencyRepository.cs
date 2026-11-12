using Microsoft.EntityFrameworkCore;
using Npgsql;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Domain.Repositories.Idempotency;

namespace PedeAqui.Infrastructure.Database.Repositories;

internal class IdempotencyRepository : IIdempotencyRepository
{
    private readonly PedeAquiDbContext _dbContext;

    public IdempotencyRepository(PedeAquiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IdempotencyRequest?> GetByKey(long userId, string key, CancellationToken cancellationToken)
    {
        return await _dbContext.IdempotencyRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(request => request.UserId == userId && request.Key == key, cancellationToken);
    }

    public async Task<bool> InsertIfKeyIsNew(IdempotencyRequest request, CancellationToken cancellationToken)
    {
        var entry = _dbContext.IdempotencyRequests.Add(request);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    public async Task<bool> TryTakeOverExpiredLock(long requestId, DateTime now, DateTime lockedUntil, CancellationToken cancellationToken)
    {
        var updatedRows = await _dbContext.IdempotencyRequests
            .Where(request => request.Id == requestId
                && request.Status == IdempotencyStatus.InProgress
                && request.LockedUntil <= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(request => request.LockedUntil, lockedUntil), cancellationToken);

        return updatedRows == 1;
    }

    public async Task MarkCompleted(long requestId, int responseStatusCode, string? responseBody, DateTime completedAt, CancellationToken cancellationToken)
    {
        await _dbContext.IdempotencyRequests
            .Where(request => request.Id == requestId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.Status, IdempotencyStatus.Completed)
                .SetProperty(request => request.ResponseStatusCode, responseStatusCode)
                .SetProperty(request => request.ResponseBody, responseBody)
                .SetProperty(request => request.CompletedAt, completedAt), cancellationToken);
    }

    public async Task DeleteInProgress(long requestId, CancellationToken cancellationToken)
    {
        await _dbContext.IdempotencyRequests
            .Where(request => request.Id == requestId && request.Status == IdempotencyStatus.InProgress)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<bool> DeleteIfExpired(long requestId, DateTime now, CancellationToken cancellationToken)
    {
        var deletedRows = await _dbContext.IdempotencyRequests
            .Where(request => request.Id == requestId && request.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);

        return deletedRows == 1;
    }

    public async Task<int> DeleteExpired(DateTime now, CancellationToken cancellationToken)
    {
        return await _dbContext.IdempotencyRequests
            .Where(request => request.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
