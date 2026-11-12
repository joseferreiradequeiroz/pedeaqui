namespace PedeAqui.Infrastructure.Messaging.Outbox;

public interface IOutboxProcessor
{
    Task<int> ProcessBatch(CancellationToken cancellationToken);
}
