using Microsoft.Extensions.DependencyInjection;
using PedeAqui.Domain.Repositories;

namespace PedeAqui.IntegrationTests.Caching;

public sealed class CommitDisconnectProbe
{
    public static readonly TimeSpan MaximumWaitForDisconnect = TimeSpan.FromSeconds(5);

    public bool Armed { get; set; }
    public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool ServerSawClientDisconnect { get; set; }
}

public sealed class CommitThenWaitForClientDisconnectUnitOfWork : IUnitOfWork
{
    private readonly IUnitOfWork _inner;
    private readonly CommitDisconnectProbe _probe;

    private CommitThenWaitForClientDisconnectUnitOfWork(IUnitOfWork inner, CommitDisconnectProbe probe)
    {
        _inner = inner;
        _probe = probe;
    }

    public static void Decorate(IServiceCollection services, CommitDisconnectProbe probe)
    {
        var original = services.Last(descriptor => descriptor.ServiceType == typeof(IUnitOfWork));

        services.AddScoped<IUnitOfWork>(serviceProvider => new CommitThenWaitForClientDisconnectUnitOfWork(
            (IUnitOfWork)ActivatorUtilities.CreateInstance(serviceProvider, original.ImplementationType!),
            probe));
    }

    public Task SaveChanges(CancellationToken cancellationToken) => _inner.SaveChanges(cancellationToken);

    public async Task Commit(CancellationToken cancellationToken)
    {
        await _inner.Commit(cancellationToken);

        if (!_probe.Armed)
            return;

        _probe.Committed.TrySetResult();

        try
        {
            await Task.Delay(CommitDisconnectProbe.MaximumWaitForDisconnect, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _probe.ServerSawClientDisconnect = true;
        }
    }

    public Task<T> ExecuteInTransaction<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        _inner.ExecuteInTransaction(operation, cancellationToken);

    public Task ExecuteInTransaction(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        _inner.ExecuteInTransaction(operation, cancellationToken);
}
