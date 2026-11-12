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
    public const string IDEMPOTENCY_KEY_HEADER = "Idempotency-Key";
    public const string CORRELATION_ID_HEADER = "X-Correlation-Id";
    public const string IDEMPOTENT_REPLAYED_HEADER = "Idempotent-Replayed";

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
    public Task<HttpResponseMessage> UpdateProduct(long id, ProductRequest request, CancellationToken cancellationToken = default) =>
        _client.PutAsJsonAsync($"{PRODUCTS}/{id}", request, cancellationToken);
    public Task<HttpResponseMessage> DeleteProduct(long id) => _client.DeleteAsync($"{PRODUCTS}/{id}");

    public Task<HttpResponseMessage> GetStock(long productId) => _client.GetAsync($"{PRODUCTS}/{productId}/stock");
    public Task<HttpResponseMessage> StockEntry(long productId, int quantity, string? idempotencyKey = null) =>
        PostWithIdempotencyKey($"{PRODUCTS}/{productId}/stock/entries", RequestBuilder.StockMovement(quantity), idempotencyKey ?? NewIdempotencyKey());
    public Task<HttpResponseMessage> StockExit(long productId, int quantity, string? idempotencyKey = null) =>
        PostWithIdempotencyKey($"{PRODUCTS}/{productId}/stock/exits", RequestBuilder.StockMovement(quantity), idempotencyKey ?? NewIdempotencyKey());

    public Task<HttpResponseMessage> CreateOrder(OrderRequest request, string? idempotencyKey = null, CancellationToken cancellationToken = default, string? correlationId = null) =>
        PostWithIdempotencyKey(ORDERS, request, idempotencyKey ?? NewIdempotencyKey(), cancellationToken, correlationId);
    public Task<HttpResponseMessage> CreateOrderWithRawBody(string rawJsonBody, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ORDERS)
        {
            Content = new StringContent(rawJsonBody, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(IDEMPOTENCY_KEY_HEADER, idempotencyKey);
        return _client.SendAsync(request);
    }
    public Task<HttpResponseMessage> CreateOrderWithoutIdempotencyKey(OrderRequest request) => _client.PostAsJsonAsync(ORDERS, request);
    public Task<HttpResponseMessage> GetOrders() => _client.GetAsync(ORDERS);
    public Task<HttpResponseMessage> GetOrder(long id) => _client.GetAsync($"{ORDERS}/{id}");
    public Task<HttpResponseMessage> UpdateOrder(long id, OrderRequest request) => _client.PutAsJsonAsync($"{ORDERS}/{id}", request);
    public Task<HttpResponseMessage> CancelOrder(long id) => _client.PatchAsync($"{ORDERS}/{id}/cancel", null);

    public async Task<RegisterUserRequest> AuthenticateAsNewCustomer()
    {
        var user = RequestBuilder.RegisterUser();
        var response = await RegisterUser(user);
        response.EnsureSuccessStatusCode();
        var body = await Read<RegisterUserResponse>(response);
        UseToken(body.Token);
        return user;
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

    public static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");

    public static bool WasReplayed(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues(IDEMPOTENT_REPLAYED_HEADER, out var values) && values.Single() == "true";
    }

    private Task<HttpResponseMessage> PostWithIdempotencyKey<TBody>(string uri, TBody body, string idempotencyKey, CancellationToken cancellationToken = default, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body, options: JsonOptions) };
        request.Headers.TryAddWithoutValidation(IDEMPOTENCY_KEY_HEADER, idempotencyKey);
        if (correlationId is not null)
            request.Headers.TryAddWithoutValidation(CORRELATION_ID_HEADER, correlationId);
        return _client.SendAsync(request, cancellationToken);
    }

    private void UseToken(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
