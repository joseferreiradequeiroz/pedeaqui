using System.Net;
using PedeAqui.Communication.Enums;
using PedeAqui.Communication.Responses;
using PedeAqui.Exception;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests.Orders;

[Collection(IntegrationCollection.Name)]
public class OrderEndpointsTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private readonly PedeAquiWebApplicationFactory _factory;

    public OrderEndpointsTests(PedeAquiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_WithoutToken_ReturnsUnauthorized()
    {
        var api = new PedeAquiApiClient(_factory.CreateClient());

        var response = await api.CreateOrder(RequestBuilder.Order((1, 1)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidOrder_ReturnsCreatedAndDecreasesStock()
    {
        var admin = await Admin();
        var pizza = await admin.CreateProductWithStock(stock: 10, price: 40m);
        var soda = await admin.CreateProductWithStock(stock: 10, price: 7.5m);
        var customer = await Customer();

        var response = await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 2), (soda.Id, 2)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await PedeAquiApiClient.Read<OrderResponse>(response);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(95m, order.Total);
        Assert.Equal(8, await admin.StockOf(pizza.Id));
        Assert.Equal(8, await admin.StockOf(soda.Id));
    }

    [Fact]
    public async Task Create_InsufficientStock_ReturnsUnprocessableEntityAndNothingChanges()
    {
        var admin = await Admin();
        var pizza = await admin.CreateProductWithStock(stock: 10);
        var soda = await admin.CreateProductWithStock(stock: 1);
        var customer = await Customer();

        var response = await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 2), (soda.Id, 5)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(10, await admin.StockOf(pizza.Id));
        Assert.Equal(1, await admin.StockOf(soda.Id));
        Assert.Empty((await PedeAquiApiClient.Read<OrdersResponse>(await customer.GetOrders())).Orders);
    }

    [Fact]
    public async Task Create_UnknownProduct_ReturnsNotFound()
    {
        var customer = await Customer();

        var response = await customer.CreateOrder(RequestBuilder.Order((999_999, 1)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutItems_ReturnsBadRequest()
    {
        var customer = await Customer();

        var response = await customer.CreateOrder(RequestBuilder.Order());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ResourceErrorMessages.ORDER_MUST_HAVE_ITEMS, await PedeAquiApiClient.ReadErrors(response));
    }

    [Fact]
    public async Task GetAll_Customer_SeesOnlyOwnOrders_AndOtherOrderIsNotFound()
    {
        var admin = await Admin();
        var product = await admin.CreateProductWithStock(stock: 10);
        var other = await Customer();
        var otherOrder = await PedeAquiApiClient.Read<OrderResponse>(await other.CreateOrder(RequestBuilder.Order((product.Id, 1))));
        var customer = await Customer();
        var ownOrder = await PedeAquiApiClient.Read<OrderResponse>(await customer.CreateOrder(RequestBuilder.Order((product.Id, 1))));

        var orders = await PedeAquiApiClient.Read<OrdersResponse>(await customer.GetOrders());

        Assert.Equal(ownOrder.Id, Assert.Single(orders.Orders).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetOrder(otherOrder.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.CancelOrder(otherOrder.Id)).StatusCode);
    }

    [Fact]
    public async Task GetAll_Admin_SeesOrdersFromEveryCustomer()
    {
        var admin = await Admin();
        var product = await admin.CreateProductWithStock(stock: 10);
        var first = await PedeAquiApiClient.Read<OrderResponse>(await (await Customer()).CreateOrder(RequestBuilder.Order((product.Id, 1))));
        var second = await PedeAquiApiClient.Read<OrderResponse>(await (await Customer()).CreateOrder(RequestBuilder.Order((product.Id, 1))));

        var orders = await PedeAquiApiClient.Read<OrdersResponse>(await admin.GetOrders());

        Assert.Contains(orders.Orders, order => order.Id == first.Id);
        Assert.Contains(orders.Orders, order => order.Id == second.Id);
    }

    [Fact]
    public async Task Update_PendingOrder_ReplacesItemsAndAdjustsStock()
    {
        var admin = await Admin();
        var pizza = await admin.CreateProductWithStock(stock: 10, price: 40m);
        var soda = await admin.CreateProductWithStock(stock: 10, price: 5m);
        var customer = await Customer();
        var order = await PedeAquiApiClient.Read<OrderResponse>(await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 3))));

        var response = await customer.UpdateOrder(order.Id, RequestBuilder.Order((pizza.Id, 1), (soda.Id, 4)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await PedeAquiApiClient.Read<OrderResponse>(await customer.GetOrder(order.Id));
        Assert.Equal(2, updated.Items.Count);
        Assert.Equal(60m, updated.Total);
        Assert.Equal(9, await admin.StockOf(pizza.Id));
        Assert.Equal(6, await admin.StockOf(soda.Id));
    }

    [Fact]
    public async Task Cancel_PendingOrder_ReturnsStockAndSecondCancelFails()
    {
        var admin = await Admin();
        var pizza = await admin.CreateProductWithStock(stock: 5);
        var customer = await Customer();
        var order = await PedeAquiApiClient.Read<OrderResponse>(await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 5))));

        var response = await customer.CancelOrder(order.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(5, await admin.StockOf(pizza.Id));
        var cancelled = await PedeAquiApiClient.Read<OrderResponse>(await customer.GetOrder(order.Id));
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.CancelOrder(order.Id)).StatusCode);
    }

    [Fact]
    public async Task Cancelling_order_awaiting_payment_returns_409()
    {
        var admin = await Admin();
        var pizza = await admin.CreateProductWithStock(stock: 5);
        var customer = await Customer();
        var order = await PedeAquiApiClient.Read<OrderResponse>(await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 2))));
        await _factory.ChangeOrder(order.Id, awaiting => awaiting.StartPayment());

        var response = await customer.CancelOrder(order.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(3, await admin.StockOf(pizza.Id));
        var current = await PedeAquiApiClient.Read<OrderResponse>(await customer.GetOrder(order.Id));
        Assert.Equal(OrderStatus.AwaitingPayment, current.Status);
    }

    [Fact]
    public async Task Updating_order_awaiting_payment_returns_409()
    {
        var admin = await Admin();
        var pizza = await admin.CreateProductWithStock(stock: 5);
        var customer = await Customer();
        var order = await PedeAquiApiClient.Read<OrderResponse>(await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 2))));
        await _factory.ChangeOrder(order.Id, awaiting => awaiting.StartPayment());

        var response = await customer.UpdateOrder(order.Id, RequestBuilder.Order((pizza.Id, 4)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(3, await admin.StockOf(pizza.Id));
    }

    [Fact]
    public async Task Confirmed_order_cannot_be_changed_or_cancelled()
    {
        var admin = await Admin();
        var pizza = await admin.CreateProductWithStock(stock: 5);
        var customer = await Customer();
        var order = await PedeAquiApiClient.Read<OrderResponse>(await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 1))));
        await _factory.ChangeOrder(order.Id, paid =>
        {
            paid.StartPayment();
            paid.ConfirmPayment();
        });

        Assert.Equal(HttpStatusCode.Conflict, (await customer.UpdateOrder(order.Id, RequestBuilder.Order((pizza.Id, 2)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.CancelOrder(order.Id)).StatusCode);
        Assert.Equal(4, await admin.StockOf(pizza.Id));
    }

    [Fact]
    public async Task Order_status_round_trips_through_the_database_for_every_new_status()
    {
        var admin = await Admin();
        var pizza = await admin.CreateProductWithStock(stock: 10);
        var customer = await Customer();
        var awaiting = await PedeAquiApiClient.Read<OrderResponse>(await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 1))));
        var expired = await PedeAquiApiClient.Read<OrderResponse>(await customer.CreateOrder(RequestBuilder.Order((pizza.Id, 1))));
        await _factory.ChangeOrder(awaiting.Id, order => order.StartPayment());
        await _factory.ChangeOrder(expired.Id, order => order.Expire());

        var awaitingRead = await PedeAquiApiClient.Read<OrderResponse>(await customer.GetOrder(awaiting.Id));
        var expiredRead = await PedeAquiApiClient.Read<OrderResponse>(await customer.GetOrder(expired.Id));

        Assert.Equal(OrderStatus.AwaitingPayment, awaitingRead.Status);
        Assert.Equal(OrderStatus.Expired, expiredRead.Status);
    }

    private async Task<PedeAquiApiClient> Admin()
    {
        var admin = new PedeAquiApiClient(_factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        return admin;
    }

    private async Task<PedeAquiApiClient> Customer()
    {
        var customer = new PedeAquiApiClient(_factory.CreateClient());
        await customer.AuthenticateAsNewCustomer();
        return customer;
    }
}
