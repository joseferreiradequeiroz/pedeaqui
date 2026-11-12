using System.Net;
using Microsoft.AspNetCore.TestHost;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.IntegrationTests.Support;
using RequestBuilder = PedeAqui.IntegrationTests.Support.RequestBuilder;
using Testcontainers.Redis;

namespace PedeAqui.IntegrationTests.Caching;

[Collection(IntegrationCollection.Name)]
public class ProductCacheTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private const string ALL_PRODUCTS_KEY = "products:all";
    private const string UNREACHABLE_REDIS = "localhost:1," + PostgresContainerFixture.REDIS_TIMEOUT_SETTINGS;

    private readonly PedeAquiWebApplicationFactory _factory;
    private readonly PostgresContainerFixture _infrastructure;

    public ProductCacheTests(PedeAquiWebApplicationFactory factory, PostgresContainerFixture infrastructure)
    {
        _factory = factory;
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task Second_catalog_read_is_served_from_cache()
    {
        var admin = await Admin(_factory);
        var product = await admin.CreateProductWithStock(stock: 5);
        var anonymous = new PedeAquiApiClient(_factory.CreateClient());

        (await anonymous.GetProducts()).EnsureSuccessStatusCode();

        Assert.True(await _factory.CacheKeyExists(ALL_PRODUCTS_KEY));
        await _factory.ChangeProduct(product.Id, stored => stored.Name = "Alterado direto no banco");
        var secondRead = await PedeAquiApiClient.Read<ProductsResponse>(await anonymous.GetProducts());
        Assert.Equal(product.Name, secondRead.Products.Single(listed => listed.Id == product.Id).Name);
    }

    [Fact]
    public async Task Product_read_by_id_is_served_from_cache()
    {
        var admin = await Admin(_factory);
        var product = await admin.CreateProductWithStock(stock: 5);
        Assert.False(await _factory.CacheKeyExists(ProductKey(product.Id)));

        (await admin.GetProduct(product.Id)).EnsureSuccessStatusCode();

        Assert.True(await _factory.CacheKeyExists(ProductKey(product.Id)));
        await _factory.ChangeProduct(product.Id, stored => stored.Name = "Alterado direto no banco");
        var secondRead = await PedeAquiApiClient.Read<ProductResponse>(await admin.GetProduct(product.Id));
        Assert.Equal(product.Name, secondRead.Name);
        Assert.Equal(5, secondRead.StockQuantity);
    }

    [Fact]
    public async Task Updating_product_invalidates_its_cache()
    {
        var admin = await Admin(_factory);
        var product = await admin.CreateProductWithStock(stock: 5);
        (await admin.GetProduct(product.Id)).EnsureSuccessStatusCode();
        (await admin.GetProducts()).EnsureSuccessStatusCode();
        Assert.True(await _factory.CacheKeyExists(ProductKey(product.Id)));
        Assert.True(await _factory.CacheKeyExists(ALL_PRODUCTS_KEY));
        var update = new ProductRequest { Name = "Nome novo", Description = product.Description, Sku = product.Sku, Price = product.Price };

        var response = await admin.UpdateProduct(product.Id, update);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await _factory.CacheKeyExists(ProductKey(product.Id)));
        Assert.False(await _factory.CacheKeyExists(ALL_PRODUCTS_KEY));
        Assert.Equal("Nome novo", (await PedeAquiApiClient.Read<ProductResponse>(await admin.GetProduct(product.Id))).Name);
    }

    [Fact]
    public async Task Client_disconnecting_after_commit_still_invalidates_the_cache()
    {
        var probe = new CommitDisconnectProbe();
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            CommitThenWaitForClientDisconnectUnitOfWork.Decorate(services, probe)));
        var admin = await Admin(_factory);
        var product = await admin.CreateProductWithStock(stock: 5);
        (await admin.GetProduct(product.Id)).EnsureSuccessStatusCode();
        Assert.True(await _factory.CacheKeyExists(ProductKey(product.Id)));
        var disconnectingAdmin = await Admin(factory.CreateClient());
        var update = new ProductRequest { Name = "Nome novo", Description = product.Description, Sku = product.Sku, Price = product.Price };
        using var clientCancellation = new CancellationTokenSource();
        probe.Armed = true;

        var abandonedRequest = disconnectingAdmin.UpdateProduct(product.Id, update, clientCancellation.Token);
        await probe.Committed.Task.WaitAsync(CommitDisconnectProbe.MaximumWaitForDisconnect);
        clientCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandonedRequest);
        Assert.True(await WaitUntilCacheKeyIsGone(ProductKey(product.Id)));
        Assert.True(probe.ServerSawClientDisconnect);
    }

    [Fact]
    public async Task Creating_order_invalidates_cached_stock()
    {
        var admin = await Admin(_factory);
        var product = await admin.CreateProductWithStock(stock: 10);
        Assert.Equal(10, await admin.StockOf(product.Id));
        Assert.True(await _factory.CacheKeyExists(ProductKey(product.Id)));
        var customer = new PedeAquiApiClient(_factory.CreateClient());
        await customer.AuthenticateAsNewCustomer();

        (await customer.CreateOrder(RequestBuilder.Order((product.Id, 3)))).EnsureSuccessStatusCode();

        Assert.False(await _factory.CacheKeyExists(ProductKey(product.Id)));
        Assert.Equal(7, await admin.StockOf(product.Id));
    }

    [Fact]
    public async Task Catalog_still_works_when_redis_is_stopped()
    {
        await using var redis = new RedisBuilder(PostgresContainerFixture.REDIS_IMAGE).Build();
        await redis.StartAsync();
        using var factory = new PedeAquiWebApplicationFactory(_infrastructure)
        {
            RedisConnectionString = $"{redis.GetConnectionString()},{PostgresContainerFixture.REDIS_TIMEOUT_SETTINGS}"
        };
        var admin = await Admin(factory);
        var product = await admin.CreateProductWithStock(stock: 5);
        (await admin.GetProducts()).EnsureSuccessStatusCode();
        Assert.True(await factory.CacheKeyExists(ALL_PRODUCTS_KEY));

        await redis.StopAsync();

        var catalog = await admin.GetProducts();
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        Assert.Contains(product.Id, (await PedeAquiApiClient.Read<ProductsResponse>(catalog)).Products.Select(listed => listed.Id));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetProduct(product.Id)).StatusCode);
        var update = new ProductRequest { Name = "Atualizado sem redis", Description = product.Description, Sku = product.Sku, Price = product.Price };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.UpdateProduct(product.Id, update)).StatusCode);
        Assert.Equal("Atualizado sem redis", (await PedeAquiApiClient.Read<ProductResponse>(await admin.GetProduct(product.Id))).Name);
    }

    [Fact]
    public async Task Api_boots_and_serves_with_unreachable_redis()
    {
        using var factory = new PedeAquiWebApplicationFactory(_infrastructure) { RedisConnectionString = UNREACHABLE_REDIS };
        var admin = await Admin(factory);
        var product = await admin.CreateProductWithStock(stock: 5);

        var response = await admin.GetProducts();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(product.Id, (await PedeAquiApiClient.Read<ProductsResponse>(response)).Products.Select(listed => listed.Id));
    }

    private static string ProductKey(long productId) => $"products:{productId}";

    private static Task<PedeAquiApiClient> Admin(PedeAquiWebApplicationFactory factory) => Admin(factory.CreateClient());

    private static async Task<PedeAquiApiClient> Admin(HttpClient client)
    {
        var admin = new PedeAquiApiClient(client);
        await admin.AuthenticateAsAdmin();
        return admin;
    }

    private async Task<bool> WaitUntilCacheKeyIsGone(string key)
    {
        var deadline = DateTime.UtcNow + CommitDisconnectProbe.MaximumWaitForDisconnect;

        while (await _factory.CacheKeyExists(key))
        {
            if (DateTime.UtcNow > deadline)
                return false;

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return true;
    }
}
