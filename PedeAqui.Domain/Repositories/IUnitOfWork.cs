namespace PedeAqui.Domain.Repositories;

public interface IUnitOfWork
{
    Task SaveChanges(CancellationToken cancellationToken);

    Task Commit(CancellationToken cancellationToken);

    Task<T> ExecuteInTransaction<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    Task ExecuteInTransaction(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}
