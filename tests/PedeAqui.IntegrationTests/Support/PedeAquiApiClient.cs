using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;

namespace PedeAqui.IntegrationTests.Support;

public sealed class PedeAquiApiClient
{
    private const string USERS = "api/users";
    private const string PRODUCTS = "api/products";
    private const string ORDERS = "api/orders";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _client;

    public PedeAquiApiClient(HttpClient client)
    {
        _client = client;
    }

    public Task<HttpResponseMessage> RegisterUser(RegisterUserRequest request) => _client.PostAsJsonAsync(USERS, request);
    public Task<HttpResponseMessage> Login(LoginRequest request) => _client.PostAsJsonAsync($"{USERS}/login", request);

    public Task<HttpResponseMessage> GetProducts() => _client.GetAsync(PRODUCTS);
    public Task<HttpResponseMessage> GetProduct(long id) => _client.GetAsync($"{PRODUCTS}/{id}");
    public Task<HttpResponseMessage> RegisterProduct(ProductRequest request) => _client.PostAsJsonAsync(PRODUCTS, request);
    public Task<HttpResponseMessage> UpdateProduct(long id, ProductRequest request) => _client.PutAsJsonAsync($"{PRODUCTS}/{id}", request);
    public Task<HttpResponseMessage> DeleteProduct(long id) => _client.DeleteAsync($"{PRODUCTS}/{id}");

    public Task<HttpResponseMessage> GetStock(long productId) => _client.GetAsync($"{PRODUCTS}/{productId}/stock");
    public Task<HttpResponseMessage> StockEntry(long productId, int quantity) => _client.PostAsJsonAsync($"{PRODUCTS}/{productId}/stock/entries", RequestBuilder.StockMovement(quantity));
    public Task<HttpResponseMessage> StockExit(long productId, int quantity) => _client.PostAsJsonAsync($"{PRODUCTS}/{productId}/stock/exits", RequestBuilder.StockMovement(quantity));

    public Task<HttpResponseMessage> CreateOrder(OrderRequest request) => _client.PostAsJsonAsync(ORDERS, request);
    public Task<HttpResponseMessage> GetOrders() => _client.GetAsync(ORDERS);
    public Task<HttpResponseMessage> GetOrder(long id) => _client.GetAsync($"{ORDERS}/{id}");
    public Task<HttpResponseMessage> UpdateOrder(long id, OrderRequest request) => _client.PutAsJsonAsync($"{ORDERS}/{id}", request);
    public Task<HttpResponseMessage> CancelOrder(long id) => _client.PatchAsync($"{ORDERS}/{id}/cancel", null);
    public Task<HttpResponseMessage> ConfirmOrder(long id) => _client.PatchAsync($"{ORDERS}/{id}/confirm", null);

    public async Task AuthenticateAsNewCustomer()
    {
        var response = await RegisterUser(RequestBuilder.RegisterUser());
        response.EnsureSuccessStatusCode();
        var body = await Read<RegisterUserResponse>(response);
        UseToken(body.Token);
    }

    public async Task AuthenticateAsAdmin()
    {
        var response = await Login(new LoginRequest
        {
            Email = PedeAquiWebApplicationFactory.ADMIN_EMAIL,
            Password = PedeAquiWebApplicationFactory.ADMIN_PASSWORD
        });
        response.EnsureSuccessStatusCode();
        var body = await Read<LoginResponse>(response);
        UseToken(body.Token);
    }

    public async Task<ProductResponse> CreateProductWithStock(int stock, decimal? price = null)
    {
        var request = RequestBuilder.Product();
        if (price.HasValue)
            request.Price = price.Value;

        var created = await Read<ProductResponse>(await RegisterProduct(request));

        if (stock > 0)
            (await StockEntry(created.Id, stock)).EnsureSuccessStatusCode();

        return created;
    }

    public async Task<int> StockOf(long productId)
    {
        var product = await Read<ProductResponse>(await GetProduct(productId));
        return product.StockQuantity;
    }

    public static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    public static async Task<List<string>> ReadErrors(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement
            .GetProperty("errors")
            .EnumerateArray()
            .Select(error => error.GetString()!)
            .ToList();
    }

    private void UseToken(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
