using System.Net;
using PedeAqui.Communication.Enums;
using PedeAqui.Communication.Responses;
using PedeAqui.Exception;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests.Stock;

[Collection(IntegrationCollection.Name)]
public class StockEndpointsTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private readonly PedeAquiWebApplicationFactory _factory;

    public StockEndpointsTests(PedeAquiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Entry_AsAdmin_IncreasesStock()
    {
        var admin = await Admin();
        var product = await admin.CreateProductWithStock(stock: 0);

        var response = await admin.StockEntry(product.Id, 12);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(12, await admin.StockOf(product.Id));
    }

    [Fact]
    public async Task Exit_MoreThanAvailable_ReturnsUnprocessableEntityAndKeepsStock()
    {
        var admin = await Admin();
        var product = await admin.CreateProductWithStock(stock: 3);

        var response = await admin.StockExit(product.Id, 5);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.StartsWith(ResourceErrorMessages.INSUFFICIENT_STOCK, (await PedeAquiApiClient.ReadErrors(response))[0]);
        Assert.Equal(3, await admin.StockOf(product.Id));
    }

    [Fact]
    public async Task Get_AfterMovements_ReturnsBalanceAndHistory()
    {
        var admin = await Admin();
        var product = await admin.CreateProductWithStock(stock: 10);
        await admin.StockExit(product.Id, 4);

        var stock = await PedeAquiApiClient.Read<StockResponse>(await admin.GetStock(product.Id));

        Assert.Equal(6, stock.Quantity);
        Assert.Equal(2, stock.Movements.Count);
        Assert.Contains(stock.Movements, movement => movement.Type == StockMovementType.Entry && movement.Quantity == 10);
        Assert.Contains(stock.Movements, movement => movement.Type == StockMovementType.Exit && movement.Quantity == 4);
    }

    [Fact]
    public async Task Entry_InvalidQuantity_ReturnsBadRequest()
    {
        var admin = await Admin();
        var product = await admin.CreateProductWithStock(stock: 0);

        var response = await admin.StockEntry(product.Id, 0);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_AsCustomer_ReturnsForbidden()
    {
        var admin = await Admin();
        var product = await admin.CreateProductWithStock(stock: 1);
        var customer = new PedeAquiApiClient(_factory.CreateClient());
        await customer.AuthenticateAsNewCustomer();

        var response = await customer.GetStock(product.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<PedeAquiApiClient> Admin()
    {
        var admin = new PedeAquiApiClient(_factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        return admin;
    }
}
