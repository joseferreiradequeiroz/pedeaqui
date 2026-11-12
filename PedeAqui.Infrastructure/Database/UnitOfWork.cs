using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PedeAqui.Domain.Repositories;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Infrastructure.Database;

internal class UnitOfWork : IUnitOfWork
{
    private readonly PedeAquiDbContext _dbContext;

    public UnitOfWork(PedeAquiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task Commit(CancellationToken cancellationToken) => SaveChanges(cancellationToken);

    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ResourceErrorMessages.CONCURRENCY_CONFLICT);
        }
        catch (DbUpdateException exception) when (TranslateDatabaseError(exception) is { } translated)
        {
            throw translated;
        }
    }

    public async Task<T> ExecuteInTransaction<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async attemptCancellationToken =>
            {
                _dbContext.ChangeTracker.Clear();

                await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, attemptCancellationToken);

                var result = await operation(attemptCancellationToken);

                await _dbContext.SaveChangesAsync(attemptCancellationToken);
                await transaction.CommitAsync(attemptCancellationToken);

                return result;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ResourceErrorMessages.CONCURRENCY_CONFLICT);
        }
        catch (DbUpdateException exception) when (TranslateDatabaseError(exception) is { } translated)
        {
            throw translated;
        }
    }

    public async Task ExecuteInTransaction(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await ExecuteInTransaction(async attemptCancellationToken =>
        {
            await operation(attemptCancellationToken);
            return true;
        }, cancellationToken);
    }

    private static PedeAquiException? TranslateDatabaseError(DbUpdateException exception)
    {
        return (exception.InnerException as PostgresException)?.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation => new ConflictException(ResourceErrorMessages.DUPLICATED_RESOURCE),
            PostgresErrorCodes.CheckViolation => new BusinessRuleException(ResourceErrorMessages.INSUFFICIENT_STOCK),
            _ => null
        };
    }
}
