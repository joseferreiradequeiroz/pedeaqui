namespace PedeAqui.Domain.Messaging;

public interface IMessagePublisher
{
    Task Publish(
        string exchange,
        string routingKey,
        string messageId,
        string type,
        string payload,
        string? correlationId,
        IDictionary<string, object?>? headers,
        CancellationToken cancellationToken);
}
