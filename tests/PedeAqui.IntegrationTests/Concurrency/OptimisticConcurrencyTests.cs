using System.Net;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Exception;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests.Concurrency;

[Collection(IntegrationCollection.Name)]
public class OptimisticConcurrencyTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private const int MAX_COLLISION_ROUNDS = 20;
    private const int INITIAL_STOCK = 10;
    private const int ORDERED_QUANTITY = 3;

    private readonly PedeAquiWebApplicationFactory _factory;

    public OptimisticConcurrencyTests(PedeAquiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Two_concurrent_product_updates_one_wins_other_gets_409()
    {
        var firstAdmin = await Admin();
        var secondAdmin = await Admin();
        var product = await firstAdmin.CreateProductWithStock(stock: 0);
        var conflictObserved = false;

        for (var round = 0; round < MAX_COLLISION_ROUNDS && !conflictObserved; round++)
        {
            var responses = await Task.WhenAll(
                firstAdmin.UpdateProduct(product.Id, UpdatedProduct(product.Sku)),
                secondAdmin.UpdateProduct(product.Id, UpdatedProduct(product.Sku)));

            Assert.DoesNotContain(responses, response => response.StatusCode == HttpStatusCode.InternalServerError);
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.NoContent);
            conflictObserved = responses.Any(response => response.StatusCode == HttpStatusCode.Conflict);
        }

        Assert.True(conflictObserved);
    }

    [Fact]
    public async Task Two_concurrent_cancellations_return_exactly_one_204_and_one_409_and_stock_returns_once()
    {
        var admin = await Admin();
        var firstCustomer = await Customer();
        var rowVersionConflictObserved = false;

        for (var round = 0; round < MAX_COLLISION_ROUNDS && !rowVersionConflictObserved; round++)
        {
            var product = await admin.CreateProductWithStock(stock: INITIAL_STOCK);
            var order = await PedeAquiApiClient.Read<OrderResponse>(
                await firstCustomer.CreateOrder(RequestBuilder.Order((product.Id, ORDERED_QUANTITY))));
            Assert.Equal(INITIAL_STOCK - ORDERED_QUANTITY, await admin.StockOf(product.Id));

            var responses = await Task.WhenAll(
                firstCustomer.CancelOrder(order.Id),
                firstCustomer.CancelOrder(order.Id));

            Assert.Equal(
                [HttpStatusCode.NoContent, HttpStatusCode.Conflict],
                responses.Select(response => response.StatusCode).OrderBy(statusCode => (int)statusCode));
            Assert.Equal(INITIAL_STOCK, await admin.StockOf(product.Id));

            var conflict = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            rowVersionConflictObserved = (await PedeAquiApiClient.ReadErrors(conflict)).Contains(ResourceErrorMessages.CONCURRENCY_CONFLICT);
        }

        Assert.True(rowVersionConflictObserved);
    }

    private static ProductRequest UpdatedProduct(string sku)
    {
        var request = RequestBuilder.Product();
        request.Sku = sku;
        return request;
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
