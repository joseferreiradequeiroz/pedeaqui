using System.Net;
using System.Net.Sockets;
using System.Text;
using PedeAqui.Infrastructure.Messaging.RabbitMq;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace PedeAqui.IntegrationTests.Support;

public sealed record RabbitMqEndpoint(string HostName, int Port, string UserName, string Password);

public sealed record ReceivedMessage(string? MessageId, string? CorrelationId, string? Type, bool Persistent, string Body);

public static class RabbitMqTestBroker
{
    public const string IMAGE = "rabbitmq:3-management";
    public const string USER_NAME = "pedeaqui";
    public const string PASSWORD = "pedeaqui";
    public const ushort AMQP_PORT = 5672;

    public static readonly int[] RetryDelaysSeconds = [5, 30, 120];

    public static RabbitMqContainer CreateContainer() => Builder().Build();

    public static RabbitMqContainer CreateContainerWithFixedHostPort() => Builder().WithPortBinding(FreeTcpPort(), AMQP_PORT).Build();

    public static RabbitMqEndpoint EndpointOf(RabbitMqContainer container) =>
        new(container.Hostname, container.GetMappedPublicPort(AMQP_PORT), USER_NAME, PASSWORD);

    public static RabbitMqOptions OptionsFor(RabbitMqEndpoint endpoint) => new()
    {
        HostName = endpoint.HostName,
        Port = endpoint.Port,
        UserName = endpoint.UserName,
        Password = endpoint.Password,
        VirtualHost = "/",
        RetryDelaysSeconds = RetryDelaysSeconds
    };

    public static async Task<IConnection> Connect(RabbitMqEndpoint endpoint)
    {
        var factory = new ConnectionFactory
        {
            HostName = endpoint.HostName,
            Port = endpoint.Port,
            UserName = endpoint.UserName,
            Password = endpoint.Password
        };

        return await factory.CreateConnectionAsync();
    }

    public static async Task PurgePaymentsQueue(RabbitMqEndpoint endpoint)
    {
        await using var connection = await Connect(endpoint);
        await using var channel = await connection.CreateChannelAsync();
        await RabbitMqTopology.Declare(channel, OptionsFor(endpoint), CancellationToken.None);
        await channel.QueuePurgeAsync(RabbitMqTopology.PAYMENTS_ORDER_CREATED_QUEUE);
    }

    public static async Task<List<ReceivedMessage>> DrainPaymentsQueue(RabbitMqEndpoint endpoint)
    {
        await using var connection = await Connect(endpoint);
        await using var channel = await connection.CreateChannelAsync();
        var messages = new List<ReceivedMessage>();

        while (await channel.BasicGetAsync(RabbitMqTopology.PAYMENTS_ORDER_CREATED_QUEUE, autoAck: true) is { } delivery)
        {
            messages.Add(new ReceivedMessage(
                delivery.BasicProperties.MessageId,
                delivery.BasicProperties.CorrelationId,
                delivery.BasicProperties.Type,
                delivery.BasicProperties.Persistent,
                Encoding.UTF8.GetString(delivery.Body.Span)));
        }

        return messages;
    }

    private static RabbitMqBuilder Builder() => new RabbitMqBuilder(IMAGE).WithUsername(USER_NAME).WithPassword(PASSWORD);

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
