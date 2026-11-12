using PedeAqui.Infrastructure.Messaging.Outbox;

namespace PedeAqui.UnitTests.Fakes;

public sealed class FakeOutboxProcessor : IOutboxProcessor
{
    private readonly Queue<Func<int>> _cycles;
    private int _callCount;

    public FakeOutboxProcessor(params Func<int>[] cycles)
    {
        _cycles = new Queue<Func<int>>(cycles);
    }

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<int> ProcessBatch(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        var cycle = _cycles.Count > 0 ? _cycles.Dequeue() : () => 0;
        return Task.FromResult(cycle());
    }
}
