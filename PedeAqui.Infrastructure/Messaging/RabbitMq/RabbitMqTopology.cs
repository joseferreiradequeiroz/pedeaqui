using RabbitMQ.Client;

namespace PedeAqui.Infrastructure.Messaging.RabbitMq;

public static class RabbitMqTopology
{
    public const string EVENTS_EXCHANGE = "pedeaqui.events";
    public const string ORDER_CREATED_ROUTING_KEY = "order.created";
    public const string PAYMENTS_ORDER_CREATED_QUEUE = "payments.order-created";
    public const string PAYMENTS_ORDER_CREATED_DLQ = "payments.order-created.dlq";
    public const string DEFAULT_EXCHANGE = "";

    private const string DEAD_LETTER_EXCHANGE_ARGUMENT = "x-dead-letter-exchange";
    private const string DEAD_LETTER_ROUTING_KEY_ARGUMENT = "x-dead-letter-routing-key";
    private const string MESSAGE_TTL_ARGUMENT = "x-message-ttl";

    public static string PaymentsOrderCreatedRetryQueue(int attempt) => $"{PAYMENTS_ORDER_CREATED_QUEUE}.retry.{attempt}";

    public static async Task Declare(IChannel channel, RabbitMqOptions options, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            exchange: EVENTS_EXCHANGE,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await DeclareDurableQueue(channel, PAYMENTS_ORDER_CREATED_DLQ, arguments: null, cancellationToken);

        await DeclareDurableQueue(channel, PAYMENTS_ORDER_CREATED_QUEUE, new Dictionary<string, object?>
        {
            [DEAD_LETTER_EXCHANGE_ARGUMENT] = DEFAULT_EXCHANGE,
            [DEAD_LETTER_ROUTING_KEY_ARGUMENT] = PAYMENTS_ORDER_CREATED_DLQ
        }, cancellationToken);

        await channel.QueueBindAsync(
            queue: PAYMENTS_ORDER_CREATED_QUEUE,
            exchange: EVENTS_EXCHANGE,
            routingKey: ORDER_CREATED_ROUTING_KEY,
            arguments: null,
            cancellationToken: cancellationToken);

        for (var index = 0; index < options.RetryDelaysSeconds.Length; index++)
        {
            await DeclareDurableQueue(channel, PaymentsOrderCreatedRetryQueue(index + 1), new Dictionary<string, object?>
            {
                [MESSAGE_TTL_ARGUMENT] = options.RetryDelaysSeconds[index] * 1000,
                [DEAD_LETTER_EXCHANGE_ARGUMENT] = DEFAULT_EXCHANGE,
                [DEAD_LETTER_ROUTING_KEY_ARGUMENT] = PAYMENTS_ORDER_CREATED_QUEUE
            }, cancellationToken);
        }
    }

    private static Task<QueueDeclareOk> DeclareDurableQueue(
        IChannel channel,
        string queue,
        IDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        return channel.QueueDeclareAsync(
            queue: queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: arguments,
            cancellationToken: cancellationToken);
    }
}
