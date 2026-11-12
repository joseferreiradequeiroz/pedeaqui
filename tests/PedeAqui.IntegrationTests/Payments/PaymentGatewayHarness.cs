using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PedeAqui.Domain.Payments;
using PedeAqui.Domain.Services;
using PedeAqui.Infrastructure;
using PedeAqui.Infrastructure.Payments;
using GatewayProgram = PedeAqui.PaymentGateway.Program;

namespace PedeAqui.IntegrationTests.Payments;

public sealed class FixedCorrelationContext : ICorrelationContext
{
    public const string CORRELATION_ID = "corr-payment-gateway-tests";

    public string CorrelationId => CORRELATION_ID;
}

public sealed class PaymentGatewayHarness : IAsyncDisposable
{
    public const double ATTEMPT_TIMEOUT_SECONDS = 0.5;
    public const int MAX_RETRY_ATTEMPTS = 3;
    public const int MINIMUM_THROUGHPUT = 5;

    private readonly WebApplicationFactory<GatewayProgram> _gatewayFactory;
    private readonly ServiceProvider _serviceProvider;
    private readonly AsyncServiceScope _scope;

    public CountingHandler Handler { get; }
    public IPaymentGateway Gateway { get; }

    private PaymentGatewayHarness(WebApplicationFactory<GatewayProgram> gatewayFactory, CountingHandler handler)
    {
        _gatewayFactory = gatewayFactory;
        Handler = handler;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaymentGateway:BaseUrl"] = gatewayFactory.Server.BaseAddress.ToString(),
                ["PaymentGateway:Bulkhead:MaxConcurrency"] = "10",
                ["PaymentGateway:Bulkhead:QueueLimit"] = "20",
                ["PaymentGateway:TotalTimeoutSeconds"] = "5",
                ["PaymentGateway:Retry:MaxRetryAttempts"] = MAX_RETRY_ATTEMPTS.ToString(),
                ["PaymentGateway:Retry:DelayMilliseconds"] = "10",
                ["PaymentGateway:CircuitBreaker:FailureRatio"] = "0.5",
                ["PaymentGateway:CircuitBreaker:MinimumThroughput"] = MINIMUM_THROUGHPUT.ToString(),
                ["PaymentGateway:CircuitBreaker:SamplingDurationSeconds"] = "30",
                ["PaymentGateway:CircuitBreaker:BreakDurationSeconds"] = "30",
                ["PaymentGateway:AttemptTimeoutSeconds"] = ATTEMPT_TIMEOUT_SECONDS.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICorrelationContext, FixedCorrelationContext>();
        services.AddPaymentGateway(configuration);
        services.AddHttpClient(PaymentGatewayResilience.CLIENT_NAME)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateAsyncScope();
        Gateway = _scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
    }

    public static async Task<PaymentGatewayHarness> Start(int failFirstCalls = 0)
    {
        var gatewayFactory = new WebApplicationFactory<GatewayProgram>();
        using (var warmupClient = gatewayFactory.CreateClient())
        {
            await warmupClient.GetAsync("/health");
        }

        var handler = new CountingHandler(failFirstCalls) { InnerHandler = gatewayFactory.Server.CreateHandler() };

        return new PaymentGatewayHarness(gatewayFactory, handler);
    }

    public async Task SetChaos(double failureRate = 0, double slowRate = 0, int slowDelayMs = 0, bool down = false)
    {
        using var client = _gatewayFactory.CreateClient();
        var response = await client.PostAsJsonAsync("/chaos", new
        {
            FailureRate = failureRate,
            SlowRate = slowRate,
            SlowDelayMs = slowDelayMs,
            Down = down
        });
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _serviceProvider.DisposeAsync();
        await _gatewayFactory.DisposeAsync();
    }
}
