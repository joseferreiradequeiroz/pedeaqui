using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedeAqui.Domain.Messaging;
using RabbitMQ.Client;

namespace PedeAqui.Infrastructure.Messaging.RabbitMq;

internal sealed class RabbitMqPublisher : IMessagePublisher, IAsyncDisposable
{
    private const string JSON_CONTENT_TYPE = "application/json";

    private readonly RabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public RabbitMqPublisher(RabbitMqConnection connection, IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _connection = connection;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Publish(
        string exchange,
        string routingKey,
        string messageId,
        string type,
        string payload,
        string? correlationId,
        IDictionary<string, object?>? headers,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.PublishTimeoutSeconds));

        var channel = await ConfirmingChannel(timeout.Token);

        var properties = new BasicProperties
        {
            Persistent = true,
            MessageId = messageId,
            CorrelationId = correlationId,
            Type = type,
            ContentType = JSON_CONTENT_TYPE,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = headers
        };

        await channel.BasicPublishAsync(
            exchange: exchange,
            routingKey: routingKey,
            mandatory: true,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(payload),
            cancellationToken: timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeChannelQuietly(_channel);
        _gate.Dispose();
    }

    private async Task DisposeChannelQuietly(IChannel? channel)
    {
        if (channel is null)
            return;

        try
        {
            await channel.DisposeAsync();
        }
        catch (System.Exception exception)
        {
            _logger.LogWarning(exception, "Failed to dispose a stale RabbitMQ channel");
        }
    }

    private async Task<IChannel> ConfirmingChannel(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } openChannel)
            return openChannel;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true } channelOpenedMeanwhile)
                return channelOpenedMeanwhile;

            await DisposeChannelQuietly(_channel);

            var connection = await _connection.Get(cancellationToken);
            _channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                cancellationToken);

            return _channel;
        }
        finally
        {
            _gate.Release();
        }
    }
}
