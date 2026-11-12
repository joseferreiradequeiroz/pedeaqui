using System.Net;
using PedeAqui.Communication.Enums;
using PedeAqui.Communication.Responses;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests.Concurrency;

[Collection(IntegrationCollection.Name)]
public class StockConcurrencyTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private const int MAX_SKU_COLLISION_ROUNDS = 20;
    private const int CONCURRENT_SKU_REGISTRATIONS = 10;

    private readonly PedeAquiWebApplicationFactory _factory;

    public StockConcurrencyTests(PedeAquiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Fifty_buyers_competing_for_ten_units_create_exactly_ten_orders()
    {
        var admin = await Admin();
        var product = await admin.CreateProductWithStock(stock: 10);
        var buyers = await Customers(50);

        var responses = await Task.WhenAll(buyers.Select(buyer => buyer.CreateOrder(RequestBuilder.Order((product.Id, 1)))));

        Assert.Equal(10, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(40, responses.Count(response => response.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.Equal(0, await admin.StockOf(product.Id));
        var stock = await PedeAquiApiClient.Read<StockResponse>(await admin.GetStock(product.Id));
        Assert.Equal(10, stock.Movements.Count(movement => movement.Type == StockMovementType.Exit));
    }

    [Fact]
    public async Task Two_orders_with_overlapping_products_in_opposite_order_do_not_deadlock()
    {
        var admin = await Admin();
        var first = await admin.CreateProductWithStock(stock: 100);
        var second = await admin.CreateProductWithStock(stock: 100);
        var buyers = await Customers(20);

        var responses = await Task.WhenAll(buyers.Select((buyer, index) => buyer.CreateOrder(index % 2 == 0
            ? RequestBuilder.Order((first.Id, 1), (second.Id, 1))
            : RequestBuilder.Order((second.Id, 1), (first.Id, 1)))));

        Assert.DoesNotContain(responses, response => response.StatusCode == HttpStatusCode.InternalServerError);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        Assert.Equal(80, await admin.StockOf(first.Id));
        Assert.Equal(80, await admin.StockOf(second.Id));
    }

    [Fact]
    public async Task Registering_two_products_with_same_sku_concurrently_returns_409_not_500()
    {
        var admins = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_SKU_REGISTRATIONS).Select(_ => Admin()));
        var conflictObserved = false;

        for (var round = 0; round < MAX_SKU_COLLISION_ROUNDS && !conflictObserved; round++)
        {
            var sku = RequestBuilder.Product().Sku;

            var responses = await Task.WhenAll(admins.Select(admin =>
            {
                var request = RequestBuilder.Product();
                request.Sku = sku;
                return admin.RegisterProduct(request);
            }));

            Assert.DoesNotContain(responses, response => response.StatusCode == HttpStatusCode.InternalServerError);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            conflictObserved = responses.Any(response => response.StatusCode == HttpStatusCode.Conflict);
        }

        Assert.True(conflictObserved);
    }

    private async Task<PedeAquiApiClient> Admin()
    {
        var admin = new PedeAquiApiClient(_factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        return admin;
    }

    private async Task<PedeAquiApiClient[]> Customers(int count)
    {
        return await Task.WhenAll(Enumerable.Range(0, count).Select(async _ =>
        {
            var customer = new PedeAquiApiClient(_factory.CreateClient());
            await customer.AuthenticateAsNewCustomer();
            return customer;
        }));
    }
}
