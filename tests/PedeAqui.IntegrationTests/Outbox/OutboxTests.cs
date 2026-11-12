using System.Net;
using System.Text.Json;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Events;
using PedeAqui.Infrastructure.Messaging;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests.Outbox;

[Collection(IntegrationCollection.Name)]
public class OutboxTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private readonly PedeAquiWebApplicationFactory _factory;

    public OutboxTests(PedeAquiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Creating_order_writes_order_created_to_outbox_in_same_transaction()
    {
        var admin = await Admin();
        var pizza = await admin.CreateProductWithStock(stock: 10, price: 40m);
        var (customer, email) = await Customer();
        var userId = await _factory.UserIdByEmail(email);

        var response = await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 2)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await PedeAquiApiClient.Read<OrderResponse>(response);
        var message = Assert.Single(await OutboxMessagesOf(userId));
        Assert.Equal(nameof(OrderCreatedEvent), message.Type);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(0, message.Attempts);
        Assert.Null(message.LastError);
        var created = JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload, OutboxJson.Options)!;
        Assert.Equal(order.Id, created.OrderId);
        Assert.Equal(userId, created.UserId);
        Assert.Equal(80m, created.Total);
        Assert.InRange(message.OccurredAt, created.OccurredAt.AddMinutes(-1), created.OccurredAt.AddMinutes(1));
    }

    [Fact]
    public async Task Failed_order_creation_writes_no_outbox_message()
    {
        var admin = await Admin();
        var pizza = await admin.CreateProductWithStock(stock: 10);
        var soda = await admin.CreateProductWithStock(stock: 1);
        var (customer, email) = await Customer();
        var userId = await _factory.UserIdByEmail(email);
        var messagesBefore = (await _factory.OutboxMessages()).Count;

        var response = await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 2), (soda.Id, 5)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty(await OutboxMessagesOf(userId));
        Assert.Equal(messagesBefore, (await _factory.OutboxMessages()).Count);
    }

    [Fact]
    public async Task Outbox_message_carries_correlation_id()
    {
        var admin = await Admin();
        var pizza = await admin.CreateProductWithStock(stock: 10);
        var (customer, email) = await Customer();
        var userId = await _factory.UserIdByEmail(email);
        var correlationId = $"outbox-{Guid.NewGuid():N}";

        var response = await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 1)), correlationId: correlationId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var message = Assert.Single(await OutboxMessagesOf(userId));
        Assert.Equal(correlationId, message.CorrelationId);
    }

    private async Task<List<OutboxMessage>> OutboxMessagesOf(long userId)
    {
        var messages = await _factory.OutboxMessages();
        return messages
            .Where(message => JsonSerializer.Deserialize<OrderCreatedEvent>(message.Payload, OutboxJson.Options)!.UserId == userId)
            .ToList();
    }

    private async Task<PedeAquiApiClient> Admin()
    {
        var admin = new PedeAquiApiClient(_factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        return admin;
    }

    private async Task<(PedeAquiApiClient Client, string Email)> Customer()
    {
        var customer = new PedeAquiApiClient(_factory.CreateClient());
        var user = await customer.AuthenticateAsNewCustomer();
        return (customer, user.Email);
    }
}
