using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PedeAqui.Communication.Requests;
using PedeAqui.Exception;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests.RateLimiting;

[Collection(IntegrationCollection.Name)]
public class RateLimitTests
{
    private const int LOGIN_LIMIT = 5;
    private const int WINDOW_SECONDS = 60;
    private const string CORRELATION_HEADER = "X-Correlation-Id";
    private const string UNREACHABLE_REDIS = "localhost:1," + PostgresContainerFixture.REDIS_TIMEOUT_SETTINGS;

    private readonly PostgresContainerFixture _infrastructure;

    public RateLimitTests(PostgresContainerFixture infrastructure)
    {
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task Sixth_login_attempt_within_window_returns_429_with_retry_after()
    {
        using var factory = FactoryWithLimits(loginLimit: LOGIN_LIMIT);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CORRELATION_HEADER, "limite-excedido-1");
        var api = new PedeAquiApiClient(client);

        for (var attempt = 1; attempt <= LOGIN_LIMIT; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Login(UnknownUser())).StatusCode);
        var response = await api.Login(UnknownUser());

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var retryAfterSeconds = int.Parse(Assert.Single(response.Headers.GetValues("Retry-After")));
        Assert.InRange(retryAfterSeconds, 1, WINDOW_SECONDS);
        var problem = await ReadProblem(response);
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.Equal(ResourceErrorMessages.RATE_LIMIT_EXCEEDED, problem.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal("limite-excedido-1", problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Limits_are_per_ip_for_login()
    {
        using var factory = FactoryWithLimits(loginLimit: 2);
        var firstIp = ApiFromIp(factory, "10.0.0.1");
        var secondIp = ApiFromIp(factory, "10.0.0.2");

        await firstIp.Login(UnknownUser());
        await firstIp.Login(UnknownUser());

        Assert.Equal(HttpStatusCode.TooManyRequests, (await firstIp.Login(UnknownUser())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondIp.Login(UnknownUser())).StatusCode);
    }

    [Fact]
    public async Task Authenticated_routes_are_limited_per_user()
    {
        using var factory = FactoryWithLimits(authenticatedLimit: 3);
        var firstUser = new PedeAquiApiClient(factory.CreateClient());
        var secondUser = new PedeAquiApiClient(factory.CreateClient());
        await firstUser.AuthenticateAsNewCustomer();
        await secondUser.AuthenticateAsNewCustomer();

        for (var call = 1; call <= 3; call++)
            Assert.Equal(HttpStatusCode.OK, (await firstUser.GetOrders()).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await firstUser.GetOrders()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondUser.GetOrders()).StatusCode);
    }

    [Fact]
    public async Task Rate_limit_fails_open_when_redis_is_down()
    {
        using var factory = FactoryWithLimits(loginLimit: 1, redisConnectionString: UNREACHABLE_REDIS);
        var api = new PedeAquiApiClient(factory.CreateClient());

        for (var attempt = 1; attempt <= 3; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await api.Login(UnknownUser())).StatusCode);
    }

    private WebApplicationFactory<Program> FactoryWithLimits(
        int loginLimit = 100_000,
        int authenticatedLimit = 100_000,
        string? redisConnectionString = null)
    {
        var factory = redisConnectionString is null
            ? new PedeAquiWebApplicationFactory(_infrastructure)
            : new PedeAquiWebApplicationFactory(_infrastructure) { RedisConnectionString = redisConnectionString };

        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimit:Login:Limit", loginLimit.ToString());
            builder.UseSetting("RateLimit:Login:WindowSeconds", WINDOW_SECONDS.ToString());
            builder.UseSetting("RateLimit:Authenticated:Limit", authenticatedLimit.ToString());
            builder.UseSetting("RateLimit:Authenticated:WindowSeconds", WINDOW_SECONDS.ToString());
            builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, ClientIpFromHeaderStartupFilter>());
        });
    }

    private static PedeAquiApiClient ApiFromIp(WebApplicationFactory<Program> factory, string ip)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ClientIpFromHeaderStartupFilter.HEADER_NAME, ip);
        return new PedeAquiApiClient(client);
    }

    private static LoginRequest UnknownUser() => new()
    {
        Email = "ninguem@pedeaqui.test",
        Password = "Senha@1234"
    };

    private static async Task<JsonElement> ReadProblem(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
