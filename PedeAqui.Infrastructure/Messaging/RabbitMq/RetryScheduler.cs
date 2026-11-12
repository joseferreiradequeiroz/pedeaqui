using Microsoft.Extensions.Options;
using PedeAqui.Domain.Messaging;

namespace PedeAqui.Infrastructure.Messaging.RabbitMq;

public sealed class RetryScheduler
{
    private readonly IMessagePublisher _publisher;
    private readonly RabbitMqOptions _options;

    public RetryScheduler(IMessagePublisher publisher, IOptions<RabbitMqOptions> options)
    {
        _publisher = publisher;
        _options = options.Value;
    }

    public bool HasRetriesLeft(ConsumedMessage message) => message.Attempt < _options.RetryDelaysSeconds.Length;

    public Task ScheduleRetry(ConsumedMessage message, CancellationToken cancellationToken)
    {
        var nextAttempt = message.Attempt + 1;

        return _publisher.Publish(
            RabbitMqTopology.DEFAULT_EXCHANGE,
            RabbitMqTopology.PaymentsOrderCreatedRetryQueue(nextAttempt),
            message.MessageId ?? string.Empty,
            message.Type ?? string.Empty,
            message.Payload,
            message.CorrelationId,
            new Dictionary<string, object?> { [ConsumedMessage.ATTEMPT_HEADER] = nextAttempt },
            cancellationToken);
    }
}
