using System.Text.Json;
using Microsoft.Extensions.Options;
using PedeAqui.Application.UseCases.Payments.Process;
using PedeAqui.Domain.Events;
using PedeAqui.Domain.Payments;
using PedeAqui.Exception.ExceptionsBase;
using PedeAqui.Infrastructure.Messaging;
using PedeAqui.Infrastructure.Messaging.RabbitMq;
using PedeAqui.Worker.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;

namespace PedeAqui.Worker.HostedServices;

public sealed class OrderCreatedConsumerService : BackgroundService
{
    private readonly RabbitMqConnection _connection;
    private readonly RetryScheduler _retryScheduler;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OrderCreatedConsumerService> _logger;
    private readonly CancellationTokenSource _processingCancellation = new();

    public OrderCreatedConsumerService(
        RabbitMqConnection connection,
        RetryScheduler retryScheduler,
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<OrderCreatedConsumerService> logger)
    {
        _connection = connection;
        _retryScheduler = retryScheduler;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public override async Task StopAsync(CancellationToken shutdownTimeoutToken)
    {
        await using var abortProcessingWhenShutdownTimesOut = shutdownTimeoutToken.Register(_processingCancellation.Cancel);
        await base.StopAsync(shutdownTimeoutToken);
    }

    public override void Dispose()
    {
        _processingCancellation.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var channel = await OpenChannelWhenBrokerIsReachable(stoppingToken);
            if (channel is null)
                break;

            await using (channel)
            {
                await ConsumeUntilStoppedOrChannelLost(channel, stoppingToken);
            }
        }

        _logger.LogInformation("Order created consumer stopped");
    }

    private async Task<IChannel?> OpenChannelWhenBrokerIsReachable(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = await _connection.Get(stoppingToken);
                return await connection.CreateChannelAsync(cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return null;
            }
            catch (System.Exception exception)
            {
                _logger.LogWarning(exception, "RabbitMQ is unreachable; trying again in {DelaySeconds} s", _options.NetworkRecoveryIntervalSeconds);
                await DelayUnlessStopping(TimeSpan.FromSeconds(_options.NetworkRecoveryIntervalSeconds), stoppingToken);
            }
        }

        return null;
    }

    private async Task ConsumeUntilStoppedOrChannelLost(IChannel channel, CancellationToken stoppingToken)
    {
        var channelLost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumerUnregistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        channel.ChannelShutdownAsync += (_, _) =>
        {
            channelLost.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => Handle(channel, delivery);
        consumer.UnregisteredAsync += (_, _) =>
        {
            consumerUnregistered.TrySetResult();
            return Task.CompletedTask;
        };

        try
        {
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: _options.PrefetchCount, global: false, stoppingToken);
            var consumerTag = await channel.BasicConsumeAsync(RabbitMqTopology.PAYMENTS_ORDER_CREATED_QUEUE, autoAck: false, consumer, stoppingToken);

            _logger.LogInformation("Consuming {Queue} with prefetch {PrefetchCount}", RabbitMqTopology.PAYMENTS_ORDER_CREATED_QUEUE, _options.PrefetchCount);

            await Task.WhenAny(channelLost.Task, Task.Delay(Timeout.Infinite, stoppingToken));

            if (stoppingToken.IsCancellationRequested)
                await StopConsumingAfterInFlightMessages(channel, consumerTag, consumerUnregistered.Task);
            else
                _logger.LogWarning("The consuming channel was closed; opening a new one");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (System.Exception exception)
        {
            _logger.LogWarning(exception, "Failed to consume {Queue}; opening a new channel", RabbitMqTopology.PAYMENTS_ORDER_CREATED_QUEUE);
            await DelayUnlessStopping(TimeSpan.FromSeconds(_options.NetworkRecoveryIntervalSeconds), stoppingToken);
        }
    }

    private async Task StopConsumingAfterInFlightMessages(IChannel channel, string consumerTag, Task consumerUnregistered)
    {
        try
        {
            await channel.BasicCancelAsync(consumerTag, noWait: false, _processingCancellation.Token);
            await consumerUnregistered.WaitAsync(_processingCancellation.Token);
            await channel.CloseAsync(_processingCancellation.Token);
        }
        catch (System.Exception exception)
        {
            _logger.LogWarning(exception, "Could not finish the in-flight messages before shutdown; the broker will redeliver the unacknowledged ones");
        }
    }

    private async Task Handle(IChannel channel, BasicDeliverEventArgs delivery)
    {
        var message = ConsumedMessage.From(delivery);

        using (LogContext.PushProperty("CorrelationId", message.CorrelationId))
        using (LogContext.PushProperty("MessageId", message.MessageId))
        {
            var decision = await Process(message, _processingCancellation.Token);
            await Settle(channel, delivery.DeliveryTag, message, decision);
        }
    }

    private async Task<DeliveryDecision> Process(ConsumedMessage message, CancellationToken processingToken)
    {
        if (!TryReadOrderCreated(message, out var messageId, out var orderCreated))
        {
            _logger.LogError("Message {MessageId} is not a valid {Type}; sending it to the DLQ", message.MessageId, nameof(OrderCreatedEvent));
            return DeliveryDecision.DeadLetter;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<MessageCorrelationContext>().CorrelationId = message.CorrelationId ?? string.Empty;

            var outcome = await scope.ServiceProvider.GetRequiredService<IProcessOrderPaymentUseCase>()
                .Execute(messageId, orderCreated.OrderId, processingToken);

            _logger.LogInformation("Payment of order {OrderId} processed: {Outcome}", orderCreated.OrderId, outcome);
            return DeliveryDecision.Ack;
        }
        catch (OperationCanceledException) when (processingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Shutdown timed out while processing order {OrderId}; returning the message to the queue", orderCreated.OrderId);
            return DeliveryDecision.Requeue;
        }
        catch (System.Exception exception) when (IsPermanent(exception))
        {
            _logger.LogError(exception, "Payment of order {OrderId} failed permanently; sending the message to the DLQ", orderCreated.OrderId);
            return DeliveryDecision.DeadLetter;
        }
        catch (System.Exception exception) when (_retryScheduler.HasRetriesLeft(message))
        {
            _logger.LogWarning(exception, "Payment of order {OrderId} failed on attempt {Attempt}; scheduling a retry", orderCreated.OrderId, message.Attempt);
            return DeliveryDecision.Retry;
        }
        catch (System.Exception exception)
        {
            _logger.LogError(exception, "Payment of order {OrderId} failed after {Attempt} retries; sending the message to the DLQ", orderCreated.OrderId, message.Attempt);
            return DeliveryDecision.DeadLetter;
        }
    }

    private async Task Settle(IChannel channel, ulong deliveryTag, ConsumedMessage message, DeliveryDecision decision)
    {
        try
        {
            switch (decision)
            {
                case DeliveryDecision.Ack:
                    await channel.BasicAckAsync(deliveryTag, multiple: false);
                    break;
                case DeliveryDecision.Retry:
                    await ScheduleRetryOrRequeue(channel, deliveryTag, message);
                    break;
                case DeliveryDecision.DeadLetter:
                    await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false);
                    break;
                case DeliveryDecision.Requeue:
                    await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
                    break;
            }
        }
        catch (System.Exception exception)
        {
            _logger.LogWarning(exception, "Could not settle message {MessageId} as {Decision}; the broker will redeliver it", message.MessageId, decision);
        }
    }

    private async Task ScheduleRetryOrRequeue(IChannel channel, ulong deliveryTag, ConsumedMessage message)
    {
        try
        {
            await _retryScheduler.ScheduleRetry(message, CancellationToken.None);
        }
        catch (System.Exception exception)
        {
            _logger.LogWarning(exception, "Could not schedule a retry for message {MessageId}; returning it to the queue", message.MessageId);
            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
            return;
        }

        await channel.BasicAckAsync(deliveryTag, multiple: false);
    }

    private static bool TryReadOrderCreated(ConsumedMessage message, out Guid messageId, out OrderCreatedEvent orderCreated)
    {
        orderCreated = default!;

        if (!Guid.TryParse(message.MessageId, out messageId))
            return false;

        try
        {
            orderCreated = JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload, OutboxJson.Options)!;
            return orderCreated is { OrderId: > 0 };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsPermanent(System.Exception exception) =>
        exception is PaymentGatewayRejectedException or NotFoundException;

    private static async Task DelayUnlessStopping(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private enum DeliveryDecision
    {
        Ack,
        Retry,
        DeadLetter,
        Requeue
    }
}
