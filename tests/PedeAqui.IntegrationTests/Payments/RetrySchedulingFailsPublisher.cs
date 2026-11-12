using Microsoft.Extensions.DependencyInjection;
using PedeAqui.Domain.Messaging;
using PedeAqui.Infrastructure.Messaging.RabbitMq;

namespace PedeAqui.IntegrationTests.Payments;

public sealed class RetrySchedulingFailures
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Record() => Interlocked.Increment(ref _count);
}

public sealed class RetrySchedulingFailsPublisher : IMessagePublisher
{
    private readonly IMessagePublisher _inner;
    private readonly RetrySchedulingFailures _failures;

    private RetrySchedulingFailsPublisher(IMessagePublisher inner, RetrySchedulingFailures failures)
    {
        _inner = inner;
        _failures = failures;
    }

    public static void Decorate(IServiceCollection services, RetrySchedulingFailures failures)
    {
        var original = services.Last(descriptor => descriptor.ServiceType == typeof(IMessagePublisher));

        services.AddSingleton<IMessagePublisher>(serviceProvider => new RetrySchedulingFailsPublisher(
            (IMessagePublisher)ActivatorUtilities.CreateInstance(serviceProvider, original.ImplementationType!),
            failures));
    }

    public Task Publish(
        string exchange,
        string routingKey,
        string messageId,
        string type,
        string payload,
        string? correlationId,
        IDictionary<string, object?>? headers,
        CancellationToken cancellationToken)
    {
        if (exchange == RabbitMqTopology.DEFAULT_EXCHANGE && routingKey.StartsWith(RabbitMqTopology.PAYMENTS_ORDER_CREATED_QUEUE, StringComparison.Ordinal))
        {
            _failures.Record();
            throw new InvalidOperationException("Agendamento de retry indisponível.");
        }

        return _inner.Publish(exchange, routingKey, messageId, type, payload, correlationId, headers, cancellationToken);
    }
}
