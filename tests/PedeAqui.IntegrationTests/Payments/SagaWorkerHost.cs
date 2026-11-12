using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PedeAqui.Infrastructure.Payments;
using PedeAqui.IntegrationTests.Messaging;
using PedeAqui.IntegrationTests.Support;
using PedeAqui.Worker;
using GatewayProgram = PedeAqui.PaymentGateway.Program;

namespace PedeAqui.IntegrationTests.Payments;

public sealed class SagaWorkerHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<GatewayProgram> _gatewayFactory;
    private readonly Dictionary<string, string?> _settings;
    private IHost? _host;

    private SagaWorkerHost(WebApplicationFactory<GatewayProgram> gatewayFactory, CountingHandler gatewayHandler, Dictionary<string, string?> settings)
    {
        _gatewayFactory = gatewayFactory;
        GatewayHandler = gatewayHandler;
        _settings = settings;
    }

    public CountingHandler GatewayHandler { get; }

    public static async Task<SagaWorkerHost> Create(PedeAquiWebApplicationFactory api, RabbitMqEndpoint broker)
    {
        var gatewayFactory = new WebApplicationFactory<GatewayProgram>();
        using (var warmupClient = gatewayFactory.CreateClient())
        {
            await warmupClient.GetAsync("/health");
        }

        var gatewayHandler = new CountingHandler(failFirstCalls: 0) { InnerHandler = gatewayFactory.Server.CreateHandler() };

        var settings = WorkerHarness.Settings(api.DatabaseConnectionString, api.RedisConnectionString, broker);
        settings["Redis:InstanceName"] = api.RedisInstanceName;
        for (var index = 0; index < SagaBrokerFixture.ShortRetryDelaysSeconds.Length; index++)
            settings[$"RabbitMq:RetryDelaysSeconds:{index}"] = SagaBrokerFixture.ShortRetryDelaysSeconds[index].ToString();
        PaymentGatewayTestSettings.Apply(settings, gatewayFactory.Server.BaseAddress.ToString());

        return new SagaWorkerHost(gatewayFactory, gatewayHandler, settings);
    }

    public int PaymentCharges => GatewayHandler.Attempts.Count(attempt => attempt.Method == HttpMethod.Post);

    public async Task Start(Action<IServiceCollection>? overrideServices = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Test" });
        builder.Configuration.AddInMemoryCollection(_settings);
        builder.Services.AddWorker(builder.Configuration);
        builder.Services.AddHttpClient(PaymentGatewayResilience.CLIENT_NAME)
            .ConfigurePrimaryHttpMessageHandler(() => GatewayHandler);
        overrideServices?.Invoke(builder.Services);

        _host = builder.Build();
        await _host.StartAsync();
    }

    public async Task Stop()
    {
        if (_host is null)
            return;

        await _host.StopAsync();
        _host.Dispose();
        _host = null;
    }

    public async Task TakeGatewayDown()
    {
        using var client = _gatewayFactory.CreateClient();
        var response = await client.PostAsJsonAsync("/chaos", new { FailureRate = 0, SlowRate = 0, SlowDelayMs = 0, Down = true });
        response.EnsureSuccessStatusCode();
    }

    public async Task ChargeDirectlyOnGateway(string idempotencyKey, decimal amount)
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

    public async ValueTask DisposeAsync()
    {
        await Stop();
        await _gatewayFactory.DisposeAsync();
    }
}
