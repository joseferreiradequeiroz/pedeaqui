using System.Net;
using PedeAqui.Communication.Responses;
using PedeAqui.Exception;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests.Products;

[Collection(IntegrationCollection.Name)]
public class ProductEndpointsTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private readonly PedeAquiWebApplicationFactory _factory;

    public ProductEndpointsTests(PedeAquiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_WithoutToken_ReturnsUnauthorized()
    {
        var api = new PedeAquiApiClient(_factory.CreateClient());

        var response = await api.RegisterProduct(RequestBuilder.Product());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_AsCustomer_ReturnsForbidden()
    {
        var api = new PedeAquiApiClient(_factory.CreateClient());
        await api.AuthenticateAsNewCustomer();

        var response = await api.RegisterProduct(RequestBuilder.Product());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Register_AsAdmin_ReturnsCreatedAndIsPubliclyVisible()
    {
        var admin = new PedeAquiApiClient(_factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        var request = RequestBuilder.Product();

        var response = await admin.RegisterProduct(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await PedeAquiApiClient.Read<ProductResponse>(response);
        var anonymous = new PedeAquiApiClient(_factory.CreateClient());
        var stored = await PedeAquiApiClient.Read<ProductResponse>(await anonymous.GetProduct(created.Id));
        Assert.Equal(request.Name, stored.Name);
        Assert.Equal(request.Sku, stored.Sku);
        Assert.Equal(request.Price, stored.Price);
        Assert.Equal(0, stored.StockQuantity);
    }

    [Fact]
    public async Task Register_DuplicatedSku_ReturnsBadRequest()
    {
        var admin = new PedeAquiApiClient(_factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        var request = RequestBuilder.Product();
        await admin.RegisterProduct(request);

        var response = await admin.RegisterProduct(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ResourceErrorMessages.PRODUCT_SKU_ALREADY_EXISTS, await PedeAquiApiClient.ReadErrors(response));
    }

    [Fact]
    public async Task Register_InvalidPrice_ReturnsBadRequest()
    {
        var admin = new PedeAquiApiClient(_factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        var request = RequestBuilder.Product();
        request.Price = 0;

        var response = await admin.RegisterProduct(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ResourceErrorMessages.PRODUCT_PRICE_MUST_BE_GREATER_THAN_ZERO, await PedeAquiApiClient.ReadErrors(response));
    }

    [Fact]
    public async Task Update_AsAdmin_PersistsChanges()
    {
        var admin = new PedeAquiApiClient(_factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        var product = await admin.CreateProductWithStock(stock: 0);
        var update = RequestBuilder.Product();

        var response = await admin.UpdateProduct(product.Id, update);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var stored = await PedeAquiApiClient.Read<ProductResponse>(await admin.GetProduct(product.Id));
        Assert.Equal(update.Name, stored.Name);
        Assert.Equal(update.Price, stored.Price);
    }

    [Fact]
    public async Task Delete_AsAdmin_RemovesFromCatalog()
    {
        var admin = new PedeAquiApiClient(_factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        var product = await admin.CreateProductWithStock(stock: 0);

        var response = await admin.DeleteProduct(product.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetProduct(product.Id)).StatusCode);
        var catalog = await PedeAquiApiClient.Read<ProductsResponse>(await admin.GetProducts());
        Assert.DoesNotContain(catalog.Products, p => p.Id == product.Id);
    }

    [Fact]
    public async Task GetById_UnknownProduct_ReturnsNotFoundWithMessage()
    {
        var api = new PedeAquiApiClient(_factory.CreateClient());

        var response = await api.GetProduct(999_999);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(ResourceErrorMessages.PRODUCT_NOT_FOUND, await PedeAquiApiClient.ReadErrors(response));
    }
}
