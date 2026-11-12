using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PedeAqui.Application.UseCases.Payments.Process;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Domain.Events;
using PedeAqui.Infrastructure.Database;
using PedeAqui.Infrastructure.Messaging;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests.Payments;

[Collection(IntegrationCollection.Name)]
public class PaymentSagaTests : IClassFixture<PedeAquiWebApplicationFactory>, IClassFixture<SagaBrokerFixture>
{
    private static readonly TimeSpan SagaTimeout = TimeSpan.FromSeconds(60);

    private readonly PedeAquiWebApplicationFactory _factory;
    private readonly SagaBrokerFixture _broker;

    public PaymentSagaTests(PedeAquiWebApplicationFactory factory, SagaBrokerFixture broker)
    {
        _factory = factory;
        _broker = broker;
    }

    [Fact]
    public async Task Order_flows_to_confirmed_end_to_end()
    {
        await _broker.PurgeAllQueues();
        await using var worker = await StartedWorker();
        var correlationId = $"saga-{Guid.NewGuid():N}";
        var (order, productId) = await CreateOrder(price: 40m, quantity: 2, correlationId);

        var confirmed = await WaitForStatus(order.Id, OrderStatus.Confirmed);

        Assert.Equal(OrderStatus.Confirmed, confirmed.Status);
        Assert.NotNull(confirmed.PaymentStartedAt);
        Assert.Equal(8, await StockOf(productId));
        var charge = Assert.Single(worker.GatewayHandler.Attempts, attempt => attempt.Method == HttpMethod.Post);
        Assert.Equal(ProcessOrderPaymentUseCase.PaymentKeyOf(order.Id), charge.IdempotencyKey);
        Assert.Equal(correlationId, charge.CorrelationId);
        Assert.Single(await InboxMessagesOf(await OutboxMessageIdOf(order.Id)));
    }

    [Fact]
    public async Task Order_with_13_cents_total_is_cancelled_and_stock_returned()
    {
        await _broker.PurgeAllQueues();
        await using var worker = await CreateWorker();
        var (order, productId) = await CreateOrder(price: 25.13m, quantity: 1);
        Assert.Equal(9, await StockOf(productId));

        await worker.Start();
        var cancelled = await WaitForStatus(order.Id, OrderStatus.Cancelled);

        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(10, await StockOf(productId));
        Assert.Single(await InboxMessagesOf(await OutboxMessageIdOf(order.Id)));
    }

    [Fact]
    public async Task Same_message_delivered_twice_charges_once_and_confirms_once()
    {
        await _broker.PurgeAllQueues();
        await using var worker = await CreateWorker();
        var (order, _) = await CreateOrder(price: 40m, quantity: 1);
        var outboxMessage = await OutboxMessageOf(order.Id);
        await _broker.PublishOrderCreated(outboxMessage.Id.ToString(), outboxMessage.CorrelationId, outboxMessage.Type, outboxMessage.Payload);

        await worker.Start();
        await WaitForStatus(order.Id, OrderStatus.Confirmed);
        await WaitUntil(async () => (await OutboxMessageOf(order.Id)).ProcessedAt is not null && await _broker.ReadyMessagesInPaymentsQueue() == 0);
        await worker.Stop();

        Assert.Equal(0u, await _broker.ReadyMessagesInPaymentsQueue());
        Assert.Equal(1, worker.PaymentCharges);
        Assert.Equal(OrderStatus.Confirmed, (await OrderOf(order.Id)).Status);
        Assert.Single(await InboxMessagesOf(outboxMessage.Id));
        Assert.Empty(await _broker.DrainDeadLetters());
    }

    [Fact]
    public async Task Gateway_down_message_ends_in_dlq_and_order_stays_awaiting_payment()
    {
        await _broker.PurgeAllQueues();
        await using var worker = await CreateWorker();
        await worker.TakeGatewayDown();
        await worker.Start();
        var (order, productId) = await CreateOrder(price: 40m, quantity: 1);
        var messageId = await OutboxMessageIdOf(order.Id);

        var deadLetter = await WaitForDeadLetter(messageId);

        Assert.Equal(SagaBrokerFixture.ShortRetryDelaysSeconds.Length, deadLetter.Attempt);
        Assert.True(worker.PaymentCharges > 0);
        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderOf(order.Id)).Status);
        Assert.Equal(9, await StockOf(productId));
        Assert.Empty(await InboxMessagesOf(messageId));
    }

    [Fact]
    public async Task Gateway_4xx_goes_straight_to_dlq_without_retries()
    {
        await _broker.PurgeAllQueues();
        await using var worker = await CreateWorker();
        var (order, _) = await CreateOrder(price: 40m, quantity: 1);
        await worker.ChargeDirectlyOnGateway(ProcessOrderPaymentUseCase.PaymentKeyOf(order.Id), amount: 1m);
        var messageId = await OutboxMessageIdOf(order.Id);

        await worker.Start();
        var deadLetter = await WaitForDeadLetter(messageId);

        Assert.Null(deadLetter.Attempt);
        Assert.Equal(1, worker.PaymentCharges);
        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderOf(order.Id)).Status);
        Assert.Empty(await InboxMessagesOf(messageId));
    }

    [Fact]
    public async Task Failed_retry_scheduling_returns_the_message_to_the_queue_instead_of_leaving_it_unacked()
    {
        await _broker.PurgeAllQueues();
        await using var worker = await CreateWorker();
        await worker.TakeGatewayDown();
        var retrySchedulingFailures = new RetrySchedulingFailures();
        await worker.Start(services => RetrySchedulingFailsPublisher.Decorate(services, retrySchedulingFailures));

        var (order, _) = await CreateOrder(price: 40m, quantity: 1);

        await WaitUntil(() => Task.FromResult(retrySchedulingFailures.Count >= 2));
        await worker.Stop();
        await _broker.PurgeAllQueues();

        Assert.Equal(OrderStatus.AwaitingPayment, (await OrderOf(order.Id)).Status);
        Assert.Empty(await InboxMessagesOf(await OutboxMessageIdOf(order.Id)));
    }

    [Fact]
    public async Task Consumer_resumes_after_the_broker_restarts()
    {
        await using var restartableBroker = RabbitMqTestBroker.CreateContainerWithFixedHostPort();
        await restartableBroker.StartAsync();
        await using var worker = await SagaWorkerHost.Create(_factory, RabbitMqTestBroker.EndpointOf(restartableBroker));
        await worker.Start();
        var (orderBeforeRestart, _) = await CreateOrder(price: 40m, quantity: 1);
        await WaitForStatus(orderBeforeRestart.Id, OrderStatus.Confirmed);

        await restartableBroker.StopAsync();
        await restartableBroker.StartAsync();
        var (orderAfterRestart, _) = await CreateOrder(price: 40m, quantity: 1);

        Assert.Equal(OrderStatus.Confirmed, (await WaitForStatus(orderAfterRestart.Id, OrderStatus.Confirmed)).Status);
    }

    private async Task<SagaWorkerHost> CreateWorker() =>
        await SagaWorkerHost.Create(_factory, _broker.Endpoint);

    private async Task<SagaWorkerHost> StartedWorker()
    {
        var worker = await CreateWorker();
        await worker.Start();
        return worker;
    }

    private async Task<(OrderResponse Order, long ProductId)> CreateOrder(decimal price, int quantity, string? correlationId = null)
    {
        var admin = new PedeAquiApiClient(_factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        var product = await admin.CreateProductWithStock(stock: 10, price: price);
        var customer = new PedeAquiApiClient(_factory.CreateClient());
        await customer.AuthenticateAsNewCustomer();

        var response = await customer.CreateOrder(RequestBuilder.Order((product.Id, quantity)), correlationId: correlationId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await PedeAquiApiClient.Read<OrderResponse>(response), product.Id);
    }

    private async Task<int> StockOf(long productId)
    {
        var admin = new PedeAquiApiClient(_factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        return await admin.StockOf(productId);
    }

    private async Task<Order> OrderOf(long orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        return await dbContext.Set<Order>().AsNoTracking().SingleAsync(order => order.Id == orderId);
    }

    private async Task<List<InboxMessage>> InboxMessagesOf(Guid messageId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        return await dbContext.Set<InboxMessage>().AsNoTracking().Where(message => message.MessageId == messageId).ToListAsync();
    }

    private async Task<OutboxMessage> OutboxMessageOf(long orderId)
    {
        var messages = await _factory.OutboxMessages();
        return messages.Single(message => JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload, OutboxJson.Options)!.OrderId == orderId);
    }

    private async Task<Guid> OutboxMessageIdOf(long orderId) => (await OutboxMessageOf(orderId)).Id;

    private async Task<Order> WaitForStatus(long orderId, OrderStatus expected)
    {
        await WaitUntil(async () => (await OrderOf(orderId)).Status == expected);
        return await OrderOf(orderId);
    }

    private async Task<DeadLetter> WaitForDeadLetter(Guid messageId)
    {
        var received = new List<DeadLetter>();
        await WaitUntil(async () =>
        {
            received.AddRange(await _broker.DrainDeadLetters());
            return received.Any(deadLetter => deadLetter.MessageId == messageId.ToString());
        });

        return received.First(deadLetter => deadLetter.MessageId == messageId.ToString());
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + SagaTimeout;

        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"A saga não chegou ao estado esperado em {SagaTimeout.TotalSeconds} s.");

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
