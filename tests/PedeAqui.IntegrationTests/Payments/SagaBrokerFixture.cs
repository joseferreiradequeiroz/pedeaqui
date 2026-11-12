using System.Text;
using PedeAqui.Infrastructure.Messaging.RabbitMq;
using PedeAqui.IntegrationTests.Support;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace PedeAqui.IntegrationTests.Payments;

public sealed record DeadLetter(string? MessageId, int? Attempt);

public sealed class SagaBrokerFixture : IAsyncLifetime
{
    public static readonly int[] ShortRetryDelaysSeconds = [1, 1, 1];

    private readonly RabbitMqContainer _container = RabbitMqTestBroker.CreateContainer();

    public RabbitMqEndpoint Endpoint => RabbitMqTestBroker.EndpointOf(_container);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await WithChannel(channel => RabbitMqTopology.Declare(channel, Options(), CancellationToken.None));
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public Task PurgeAllQueues() => WithChannel(async channel =>
    {
        await channel.QueuePurgeAsync(RabbitMqTopology.PAYMENTS_ORDER_CREATED_QUEUE);
        await channel.QueuePurgeAsync(RabbitMqTopology.PAYMENTS_ORDER_CREATED_DLQ);
        for (var attempt = 1; attempt <= ShortRetryDelaysSeconds.Length; attempt++)
            await channel.QueuePurgeAsync(RabbitMqTopology.PaymentsOrderCreatedRetryQueue(attempt));
    });

    public Task PublishOrderCreated(string messageId, string? correlationId, string type, string payload) => WithChannel(channel =>
        channel.BasicPublishAsync(
            RabbitMqTopology.EVENTS_EXCHANGE,
            RabbitMqTopology.ORDER_CREATED_ROUTING_KEY,
            mandatory: true,
            new BasicProperties { MessageId = messageId, CorrelationId = correlationId, Type = type, Persistent = true },
            Encoding.UTF8.GetBytes(payload)).AsTask());

    public async Task<uint> ReadyMessagesInPaymentsQueue()
    {
        uint ready = 0;
        await WithChannel(async channel => ready = (await channel.QueueDeclarePassiveAsync(RabbitMqTopology.PAYMENTS_ORDER_CREATED_QUEUE)).MessageCount);
        return ready;
    }

    public async Task<List<DeadLetter>> DrainDeadLetters()
    {
        var deadLetters = new List<DeadLetter>();

        await WithChannel(async channel =>
        {
            while (await channel.BasicGetAsync(RabbitMqTopology.PAYMENTS_ORDER_CREATED_DLQ, autoAck: true) is { } delivery)
            {
                var headers = delivery.BasicProperties.Headers;
                int? attempt = headers is not null && headers.TryGetValue(ConsumedMessage.ATTEMPT_HEADER, out var value) ? Convert.ToInt32(value) : null;
                deadLetters.Add(new DeadLetter(delivery.BasicProperties.MessageId, attempt));
            }
        });

        return deadLetters;
    }

    private RabbitMqOptions Options()
    {
        var options = RabbitMqTestBroker.OptionsFor(Endpoint);
        options.RetryDelaysSeconds = ShortRetryDelaysSeconds;
        return options;
    }

    private async Task WithChannel(Func<IChannel, Task> action)
    {
        await using var connection = await RabbitMqTestBroker.Connect(Endpoint);
        await using var channel = await connection.CreateChannelAsync();
        await action(channel);
    }
}
