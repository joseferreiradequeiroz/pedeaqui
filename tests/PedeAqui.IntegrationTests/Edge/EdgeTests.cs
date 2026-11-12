using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PedeAqui.Communication.Requests;
using PedeAqui.Exception;
using PedeAqui.IntegrationTests.Support;
using RequestBuilder = PedeAqui.IntegrationTests.Support.RequestBuilder;

namespace PedeAqui.IntegrationTests.Edge;

[Collection(IntegrationCollection.Name)]
public class EdgeTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private const string CORRELATION_HEADER = "X-Correlation-Id";
    private const int MAX_ORDER_ITEMS = 50;
    private const int MAX_ITEM_QUANTITY = 1000;
    private const int BODY_ABOVE_LIMIT_IN_BYTES = 70 * 1024;
    private const int BODY_WITHIN_LIMIT_IN_BYTES = 32 * 1024;

    private readonly PedeAquiWebApplicationFactory _factory;

    public EdgeTests(PedeAquiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Response_echoes_incoming_correlation_id()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(CORRELATION_HEADER, "pedido-abc-123");

        var response = await client.GetAsync("api/products");

        Assert.Equal("pedido-abc-123", Assert.Single(response.Headers.GetValues(CORRELATION_HEADER)));
    }

    [Fact]
    public async Task Response_generates_correlation_id_when_missing()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("api/products");

        var generated = Assert.Single(response.Headers.GetValues(CORRELATION_HEADER));
        Assert.True(Guid.TryParseExact(generated, "N", out _));
    }

    [Theory]
    [InlineData("tem espaco e !")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public async Task Response_replaces_invalid_incoming_correlation_id(string invalidCorrelationId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(CORRELATION_HEADER, invalidCorrelationId);

        var response = await client.GetAsync("api/products");

        var generated = Assert.Single(response.Headers.GetValues(CORRELATION_HEADER));
        Assert.NotEqual(invalidCorrelationId, generated);
        Assert.True(Guid.TryParseExact(generated, "N", out _));
    }

    [Fact]
    public async Task Error_problem_details_contains_trace_and_correlation_ids()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(CORRELATION_HEADER, "erro-correlacionado-1");

        var response = await client.PostAsJsonAsync("api/users/login", new LoginRequest
        {
            Email = "ninguem@pedeaqui.test",
            Password = "Senha@1234"
        });

        Assert.False(response.IsSuccessStatusCode);
        var problem = await ReadProblem(response);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal("erro-correlacionado-1", problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Framework_generated_problem_details_also_contain_correlation_id()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(CORRELATION_HEADER, "json-invalido-1");
        var malformedJson = new StringContent("{ nao e json", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/users/login", malformedJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadProblem(response);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal("json-invalido-1", problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Unhandled_exception_returns_generic_500_with_ids_and_no_internal_details()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddControllers().AddApplicationPart(typeof(EdgeProbeController).Assembly)));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CORRELATION_HEADER, "falha-inesperada-1");

        var response = await client.GetAsync("test/edge/unhandled");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(EdgeProbeController.INTERNAL_DETAIL, rawBody);
        Assert.DoesNotContain("InvalidOperationException", rawBody);
        Assert.DoesNotContain(" at ", rawBody);
        using var document = JsonDocument.Parse(rawBody);
        var problem = document.RootElement;
        Assert.Equal(ResourceErrorMessages.UNKNOWN_ERROR, problem.GetProperty("detail").GetString());
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal("falha-inesperada-1", problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Order_with_51_items_returns_400()
    {
        var api = new PedeAquiApiClient(_factory.CreateClient());
        await api.AuthenticateAsNewCustomer();
        var items = Enumerable.Range(1, MAX_ORDER_ITEMS + 1).Select(productId => ((long)productId, 1)).ToArray();

        var response = await api.CreateOrder(RequestBuilder.Order(items));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await PedeAquiApiClient.ReadErrors(response);
        Assert.Contains(string.Format(ResourceErrorMessages.ORDER_TOO_MANY_ITEMS, MAX_ORDER_ITEMS), errors);
    }

    [Fact]
    public async Task Order_item_with_quantity_above_limit_returns_400()
    {
        var api = new PedeAquiApiClient(_factory.CreateClient());
        await api.AuthenticateAsNewCustomer();

        var response = await api.CreateOrder(RequestBuilder.Order((1, MAX_ITEM_QUANTITY + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await PedeAquiApiClient.ReadErrors(response);
        Assert.Contains(string.Format(ResourceErrorMessages.ORDER_ITEM_QUANTITY_ABOVE_LIMIT, MAX_ITEM_QUANTITY), errors);
    }

    [Fact]
    public async Task Body_larger_than_limit_returns_413()
    {
        var client = _factory.CreateClient();
        var api = new PedeAquiApiClient(client);
        await api.AuthenticateAsNewCustomer();
        client.DefaultRequestHeaders.Add(CORRELATION_HEADER, "corpo-grande-1");

        var response = await client.PostAsync("api/orders", OrderBodyWithPadding(BODY_ABOVE_LIMIT_IN_BYTES));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var problem = await ReadProblem(response);
        Assert.Equal(ResourceErrorMessages.REQUEST_BODY_TOO_LARGE, problem.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal("corpo-grande-1", problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Body_within_limit_is_not_rejected_as_too_large()
    {
        var client = _factory.CreateClient();
        var api = new PedeAquiApiClient(client);
        await api.AuthenticateAsNewCustomer();

        var response = await client.PostAsync("api/orders", OrderBodyWithPadding(BODY_WITHIN_LIMIT_IN_BYTES));

        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Request_exceeding_timeout_returns_504()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ApiEdge:RequestTimeoutSeconds", "1");
            builder.ConfigureTestServices(services => services.AddControllers().AddApplicationPart(typeof(EdgeProbeController).Assembly));
        });
        var client = factory.CreateClient();

        var response = await client.GetAsync("test/edge/slow");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    private static StringContent OrderBodyWithPadding(int paddingLengthInBytes)
    {
        var padding = new string('x', paddingLengthInBytes);
        var json = $$"""{"items":[{"productId":1,"quantity":1}],"padding":"{{padding}}"}""";
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static async Task<JsonElement> ReadProblem(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
