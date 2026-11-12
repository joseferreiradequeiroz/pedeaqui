using PedeAqui.Domain.Messaging;

namespace PedeAqui.UnitTests.Fakes;

public sealed class FakeOutbox : IOutbox
{
    private readonly FakeUnitOfWork _unitOfWork;

    public FakeOutbox(FakeUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public List<object> Messages { get; } = [];
    public int SaveCountAtLastEnqueue { get; private set; }

    public IEnumerable<TEvent> Enqueued<TEvent>() => Messages.OfType<TEvent>();

    public Task Enqueue<TEvent>(TEvent @event, CancellationToken cancellationToken)
    {
        Messages.Add(@event!);
        SaveCountAtLastEnqueue = _unitOfWork.SaveCount;
        return Task.CompletedTask;
    }
}
