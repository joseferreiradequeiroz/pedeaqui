using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PedeAqui.Application.UseCases;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Entities;
using PedeAqui.Infrastructure.Database;
using PedeAqui.Infrastructure.Payments;
using PedeAqui.IntegrationTests.Messaging;
using PedeAqui.IntegrationTests.Payments;
using PedeAqui.IntegrationTests.Support;
using PedeAqui.Worker;
using GatewayProgram = PedeAqui.PaymentGateway.Program;

namespace PedeAqui.IntegrationTests.Jobs;

public sealed class JobsHarness : IAsyncDisposable
{
    private static readonly RabbitMqEndpoint UnusedBroker = new("localhost", 5672, "unused", "unused");

    private readonly PedeAquiWebApplicationFactory _api;
    private readonly WebApplicationFactory<GatewayProgram> _gatewayFactory;
    private readonly ServiceProvider _services;

    private JobsHarness(PedeAquiWebApplicationFactory api, WebApplicationFactory<GatewayProgram> gatewayFactory, FakeTimeProvider clock, ServiceProvider services)
    {
        _api = api;
        _gatewayFactory = gatewayFactory;
        Clock = clock;
        _services = services;
    }

    public FakeTimeProvider Clock { get; }

    public static async Task<JobsHarness> Create(PedeAquiWebApplicationFactory api)
    {
        var gatewayFactory = new WebApplicationFactory<GatewayProgram>();
        using (var warmupClient = gatewayFactory.CreateClient())
        {
            await warmupClient.GetAsync("/health");
        }

        var gatewayHandler = new CountingHandler(failFirstCalls: 0) { InnerHandler = gatewayFactory.Server.CreateHandler() };
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var settings = WorkerHarness.Settings(api.DatabaseConnectionString, api.RedisConnectionString, UnusedBroker);
        settings["Redis:InstanceName"] = api.RedisInstanceName;
        PaymentGatewayTestSettings.Apply(settings, gatewayFactory.Server.BaseAddress.ToString());
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddWorker(configuration);
        services.AddSingleton<TimeProvider>(clock);
        services.AddHttpClient(PaymentGatewayResilience.CLIENT_NAME)
            .ConfigurePrimaryHttpMessageHandler(() => gatewayHandler);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        return new JobsHarness(api, gatewayFactory, clock, provider);
    }

    public async Task<int> Run<TUseCase>() where TUseCase : IBackgroundJobUseCase
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TUseCase>().Execute(CancellationToken.None);
    }

    public async Task<(OrderResponse Order, long ProductId)> CreateOrder(decimal price, int quantity)
    {
        var admin = new PedeAquiApiClient(_api.CreateClient());
        await admin.AuthenticateAsAdmin();
        var product = await admin.CreateProductWithStock(stock: 10, price: price);
        var customer = new PedeAquiApiClient(_api.CreateClient());
        await customer.AuthenticateAsNewCustomer();

        var response = await customer.CreateOrder(RequestBuilder.Order((product.Id, quantity)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await PedeAquiApiClient.Read<OrderResponse>(response), product.Id);
    }

    public async Task<int> StockOf(long productId)
    {
        var admin = new PedeAquiApiClient(_api.CreateClient());
        await admin.AuthenticateAsAdmin();
        return await admin.StockOf(productId);
    }

    public async Task<Order> OrderOf(long orderId)
    {
        using var scope = _api.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        return await dbContext.Set<Order>().AsNoTracking().SingleAsync(order => order.Id == orderId);
    }

    public Task MovePaymentToAwaiting(long orderId) => _api.ChangeOrder(orderId, order => order.StartPayment());

    public async Task ChargeOnGateway(string idempotencyKey, decimal amount)
    {
        using var client = _gatewayFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/payments")
        {
            Content = JsonContent.Create(new { OrderReference = idempotencyKey, Amount = amount })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task TakeGatewayDown()
    {
        using var client = _gatewayFactory.CreateClient();
        var response = await client.PostAsJsonAsync("/chaos", new { FailureRate = 0, SlowRate = 0, SlowDelayMs = 0, Down = true });
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _gatewayFactory.DisposeAsync();
    }
}
