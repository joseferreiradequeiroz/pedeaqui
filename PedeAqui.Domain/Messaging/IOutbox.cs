namespace PedeAqui.Domain.Messaging;

public interface IOutbox
{
    Task Enqueue<TEvent>(TEvent @event, CancellationToken cancellationToken);
}
