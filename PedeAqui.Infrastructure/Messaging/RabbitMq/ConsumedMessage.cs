using System.Text;
using RabbitMQ.Client.Events;

namespace PedeAqui.Infrastructure.Messaging.RabbitMq;

public sealed record ConsumedMessage(string? MessageId, string? CorrelationId, string? Type, string Payload, int Attempt)
{
    public const string ATTEMPT_HEADER = "x-attempt";

    public static ConsumedMessage From(BasicDeliverEventArgs delivery)
    {
        var properties = delivery.BasicProperties;

        return new ConsumedMessage(
            properties.MessageId,
            properties.CorrelationId,
            properties.Type,
            Encoding.UTF8.GetString(delivery.Body.Span),
            AttemptOf(properties.Headers));
    }

    private static int AttemptOf(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(ATTEMPT_HEADER, out var value))
            return 0;

        return value switch
        {
            int attempt => attempt,
            long attempt => (int)attempt,
            byte[] text when int.TryParse(Encoding.UTF8.GetString(text), out var attempt) => attempt,
            _ => 0
        };
    }
}
