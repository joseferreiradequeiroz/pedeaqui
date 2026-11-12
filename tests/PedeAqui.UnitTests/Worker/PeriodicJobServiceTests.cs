using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PedeAqui.Application.UseCases;
using PedeAqui.Worker.HostedServices;

namespace PedeAqui.UnitTests.Worker;

public class PeriodicJobServiceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Runs_the_first_cycle_without_waiting_for_the_interval()
    {
        var job = new ScriptedJob(() => 1);
        using var service = ServiceWith(job, TimeSpan.FromHours(1));

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => job.CallCount >= 1);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, job.CallCount);
    }

    [Fact]
    public async Task Keeps_running_cycles_after_one_throws()
    {
        var job = new ScriptedJob(() => throw new InvalidOperationException("database down"), () => 0);
        using var service = ServiceWith(job, TimeSpan.FromMilliseconds(10));

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => job.CallCount >= 3);
        await service.StopAsync(CancellationToken.None);

        Assert.True(job.CallCount >= 3);
    }

    [Fact]
    public async Task Resolves_the_job_from_a_new_scope_on_every_cycle()
    {
        var job = new ScriptedJob(() => 0);
        var scopes = new CountingScopeFactory();
        using var service = ServiceWith(job, TimeSpan.FromMilliseconds(10), scopes);

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => job.CallCount >= 3);
        await service.StopAsync(CancellationToken.None);

        Assert.True(scopes.Created >= 3);
        Assert.Equal(scopes.Created, scopes.Disposed);
    }

    private static ScriptedJobService ServiceWith(ScriptedJob job, TimeSpan interval, CountingScopeFactory? scopes = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IScriptedJob>(_ => job);
        var provider = services.BuildServiceProvider();
        scopes ??= new CountingScopeFactory();
        scopes.Wrap(provider.GetRequiredService<IServiceScopeFactory>());

        return new ScriptedJobService(scopes, interval);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private interface IScriptedJob : IBackgroundJobUseCase
    {
    }

    private sealed class ScriptedJob : IScriptedJob
    {
        private readonly Queue<Func<int>> _cycles;
        private readonly Func<int> _afterScript;
        private int _callCount;

        public ScriptedJob(params Func<int>[] cycles)
        {
            _cycles = new Queue<Func<int>>(cycles);
            _afterScript = cycles[^1];
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<int> Execute(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            var cycle = _cycles.TryDequeue(out var next) ? next : _afterScript;
            return Task.FromResult(cycle());
        }
    }

    private sealed class ScriptedJobService : PeriodicJobService<IScriptedJob>
    {
        public ScriptedJobService(IServiceScopeFactory scopeFactory, TimeSpan interval)
            : base(scopeFactory, NullLogger<ScriptedJobService>.Instance, "scripted job", interval)
        {
        }
    }

    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        private IServiceScopeFactory? _inner;
        private int _created;
        private int _disposed;

        public void Wrap(IServiceScopeFactory inner)
        {
            _inner = inner;
        }

        public int Created => Volatile.Read(ref _created);
        public int Disposed => Volatile.Read(ref _disposed);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _created);
            return new CountingScope(_inner!.CreateScope(), () => Interlocked.Increment(ref _disposed));
        }
    }

    private sealed class CountingScope : IServiceScope
    {
        private readonly IServiceScope _inner;
        private readonly Action _onDispose;

        public CountingScope(IServiceScope inner, Action onDispose)
        {
            _inner = inner;
            _onDispose = onDispose;
        }

        public IServiceProvider ServiceProvider => _inner.ServiceProvider;

        public void Dispose()
        {
            _inner.Dispose();
            _onDispose();
        }
    }
}
