using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Events;
using PedeAqui.Infrastructure.Messaging;
using PedeAqui.Infrastructure.Messaging.RabbitMq;
using PedeAqui.IntegrationTests.Support;
using PedeAqui.Worker;
using PedeAqui.Worker.HostedServices;

namespace PedeAqui.IntegrationTests.Messaging;

[Collection(IntegrationCollection.Name)]
public class OutboxPublisherTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private static readonly TimeSpan BrokerRecoveryTimeout = TimeSpan.FromSeconds(60);

    private readonly PostgresContainerFixture _fixture;
    private readonly PedeAquiWebApplicationFactory _factory;

    public OutboxPublisherTests(PostgresContainerFixture fixture, PedeAquiWebApplicationFactory factory)
    {
        _fixture = fixture;
        _factory = factory;
    }

    [Fact]
    public async Task Published_outbox_message_reaches_payments_queue_with_message_and_correlation_ids()
    {
        await RabbitMqTestBroker.PurgePaymentsQueue(_fixture.RabbitMq);
        await using var worker = WorkerFor(_factory, _fixture.RabbitMq);
        var correlationId = $"publish-{Guid.NewGuid():N}";
        var order = await CreateOrder(_factory, correlationId);
        var outboxMessage = await OutboxMessageOf(worker, order.Id);

        await worker.ProcessBatch();

        var received = await RabbitMqTestBroker.DrainPaymentsQueue(_fixture.RabbitMq);
        var message = Assert.Single(received, message => message.MessageId == outboxMessage.Id.ToString());
        Assert.Equal(correlationId, message.CorrelationId);
        Assert.Equal(nameof(OrderCreatedEvent), message.Type);
        Assert.True(message.Persistent);
        var created = JsonSerializer.Deserialize<OrderCreatedEvent>(message.Body, OutboxJson.Options)!;
        Assert.Equal(order.Id, created.OrderId);
    }

    [Fact]
    public async Task Message_is_marked_processed_after_publish()
    {
        await RabbitMqTestBroker.PurgePaymentsQueue(_fixture.RabbitMq);
        await using var worker = WorkerFor(_factory, _fixture.RabbitMq);
        var order = await CreateOrder(_factory);
        Assert.Null((await OutboxMessageOf(worker, order.Id)).ProcessedAt);

        var published = await worker.ProcessBatch();

        Assert.True(published >= 1);
        var processed = await OutboxMessageOf(worker, order.Id);
        Assert.NotNull(processed.ProcessedAt);
        Assert.Equal(0, processed.Attempts);
        Assert.Null(processed.LastError);
        Assert.DoesNotContain(await worker.OutboxMessages(), message => message.ProcessedAt is null);
    }

    [Fact]
    public async Task Two_processors_in_parallel_never_publish_same_message_twice()
    {
        const int MESSAGE_COUNT = 100;
        await RabbitMqTestBroker.PurgePaymentsQueue(_fixture.RabbitMq);
        var settings = WorkerHarness.Settings(_fixture.CreateDatabaseConnectionString(), _fixture.RedisConnectionString, _fixture.RabbitMq, batchSize: 10);
        await using var firstWorker = WorkerHarness.Create(settings);
        await using var secondWorker = WorkerHarness.Create(settings);
        await firstWorker.MigrateDatabase();
        await firstWorker.AddOutboxMessages(Enumerable.Range(1, MESSAGE_COUNT).Select(OrderCreatedOutboxMessage));

        await Task.WhenAll(Task.Run(() => DrainOutbox(firstWorker)), Task.Run(() => DrainOutbox(secondWorker)));

        var received = await RabbitMqTestBroker.DrainPaymentsQueue(_fixture.RabbitMq);
        Assert.Equal(MESSAGE_COUNT, received.Count);
        Assert.Equal(MESSAGE_COUNT, received.Select(message => message.MessageId).Distinct().Count());
        var outbox = await firstWorker.OutboxMessages();
        Assert.All(outbox, message => Assert.NotNull(message.ProcessedAt));
        Assert.Equal(outbox.Select(message => message.Id.ToString()).Order(), received.Select(message => message.MessageId).Order());
    }

    [Fact]
    public async Task Order_is_created_while_broker_is_down_and_published_after_it_returns()
    {
        await using var broker = RabbitMqTestBroker.CreateContainerWithFixedHostPort();
        await broker.StartAsync();
        var endpoint = RabbitMqTestBroker.EndpointOf(broker);
        await using var factory = new PedeAquiWebApplicationFactory(_fixture);
        await using var worker = WorkerFor(factory, endpoint);
        await CreateOrder(factory);
        Assert.Equal(1, await worker.ProcessBatch());

        await broker.StopAsync();
        var order = await CreateOrder(factory);
        var publishedWhileDown = await worker.ProcessBatch();

        Assert.Equal(0, publishedWhileDown);
        var pending = Assert.Single(await worker.OutboxMessages(), message => message.ProcessedAt is null);
        Assert.Equal(order.Id, JsonSerializer.Deserialize<OrderCreatedEvent>(pending.Payload, OutboxJson.Options)!.OrderId);
        Assert.True(pending.Attempts > 0);
        Assert.False(string.IsNullOrWhiteSpace(pending.LastError));

        await broker.StartAsync();
        var publishedAfterReturn = await ProcessUntilPublished(worker);

        Assert.Equal(1, publishedAfterReturn);
        var processed = await OutboxMessageOf(worker, order.Id);
        Assert.NotNull(processed.ProcessedAt);
        var received = await RabbitMqTestBroker.DrainPaymentsQueue(endpoint);
        Assert.Contains(received, message => message.MessageId == processed.Id.ToString());
    }

    [Fact]
    public async Task Topology_declaration_is_idempotent()
    {
        await using var connection = await RabbitMqTestBroker.Connect(_fixture.RabbitMq);
        await using var channel = await connection.CreateChannelAsync();
        var options = RabbitMqTestBroker.OptionsFor(_fixture.RabbitMq);

        await RabbitMqTopology.Declare(channel, options, CancellationToken.None);
        await RabbitMqTopology.Declare(channel, options, CancellationToken.None);

        await channel.ExchangeDeclarePassiveAsync(RabbitMqTopology.EVENTS_EXCHANGE);
        await channel.QueueDeclarePassiveAsync(RabbitMqTopology.PAYMENTS_ORDER_CREATED_QUEUE);
        await channel.QueueDeclarePassiveAsync(RabbitMqTopology.PAYMENTS_ORDER_CREATED_DLQ);
        for (var attempt = 1; attempt <= RabbitMqTestBroker.RetryDelaysSeconds.Length; attempt++)
            await channel.QueueDeclarePassiveAsync(RabbitMqTopology.PaymentsOrderCreatedRetryQueue(attempt));
    }

    [Fact]
    public async Task Worker_host_publishes_pending_outbox_messages()
    {
        await RabbitMqTestBroker.PurgePaymentsQueue(_fixture.RabbitMq);
        var settings = WorkerHarness.Settings(_factory.DatabaseConnectionString, _fixture.RedisConnectionString, _fixture.RabbitMq);
        await using var worker = WorkerHarness.Create(settings);
        var order = await CreateOrder(_factory);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Test" });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddWorker(builder.Configuration);
        builder.Services.Remove(builder.Services.Single(service => service.ImplementationType == typeof(OrderCreatedConsumerService)));
        using var host = builder.Build();

        await host.StartAsync();
        var processed = await WaitUntilProcessed(worker, order.Id);
        await host.StopAsync();

        Assert.NotNull(processed.ProcessedAt);
        var received = await RabbitMqTestBroker.DrainPaymentsQueue(_fixture.RabbitMq);
        Assert.Contains(received, message => message.MessageId == processed.Id.ToString());
    }

    private static WorkerHarness WorkerFor(PedeAquiWebApplicationFactory factory, RabbitMqEndpoint rabbitMq) =>
        WorkerHarness.Create(WorkerHarness.Settings(factory.DatabaseConnectionString, factory.RedisConnectionString, rabbitMq));

    private static async Task<OrderResponse> CreateOrder(PedeAquiWebApplicationFactory factory, string? correlationId = null)
    {
        var admin = new PedeAquiApiClient(factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        var pizza = await admin.CreateProductWithStock(stock: 10, price: 40m);
        var customer = new PedeAquiApiClient(factory.CreateClient());
        await customer.AuthenticateAsNewCustomer();

        var response = await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 1)), correlationId: correlationId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await PedeAquiApiClient.Read<OrderResponse>(response);
    }

    private static async Task<OutboxMessage> OutboxMessageOf(WorkerHarness worker, long orderId)
    {
        var messages = await worker.OutboxMessages();
        return messages.Single(message => JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload, OutboxJson.Options)!.OrderId == orderId);
    }

    private static OutboxMessage OrderCreatedOutboxMessage(int index)
    {
        var occurredAt = DateTime.UtcNow.AddMilliseconds(index);
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = nameof(OrderCreatedEvent),
            Payload = JsonSerializer.Serialize(new OrderCreatedEvent(index, index, 10m, occurredAt), OutboxJson.Options),
            CorrelationId = $"parallel-{index}",
            OccurredAt = occurredAt
        };
    }

    private static async Task DrainOutbox(WorkerHarness worker)
    {
        while (await worker.ProcessBatch() > 0)
        {
        }
    }

    private static async Task<int> ProcessUntilPublished(WorkerHarness worker)
    {
        var deadline = DateTime.UtcNow + BrokerRecoveryTimeout;

        while (true)
        {
            var published = await worker.ProcessBatch();
            if (published > 0 || DateTime.UtcNow > deadline)
                return published;

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    private static async Task<OutboxMessage> WaitUntilProcessed(WorkerHarness worker, long orderId)
    {
        var deadline = DateTime.UtcNow + BrokerRecoveryTimeout;

        while (true)
        {
            var message = await OutboxMessageOf(worker, orderId);
            if (message.ProcessedAt is not null || DateTime.UtcNow > deadline)
                return message;

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
