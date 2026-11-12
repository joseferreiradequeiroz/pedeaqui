using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using PedeAqui.Domain.Entities;
using PedeAqui.Infrastructure.Database;
using PedeAqui.Infrastructure.Messaging.Outbox;
using PedeAqui.IntegrationTests.Support;
using PedeAqui.Worker;

namespace PedeAqui.IntegrationTests.Messaging;

public sealed class WorkerHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private WorkerHarness(ServiceProvider services)
    {
        _services = services;
    }

    public static Dictionary<string, string?> Settings(string databaseConnectionString, string redisConnectionString, RabbitMqEndpoint rabbitMq, int batchSize = 50) => new()
    {
        ["ConnectionStrings:Connection"] = databaseConnectionString,
        ["ConnectionStrings:Redis"] = redisConnectionString,
        ["Redis:InstanceName"] = $"worker-test-{Guid.NewGuid():N}:",
        ["Redis:TimeoutMilliseconds"] = "300",
        ["Redis:FailureRatio"] = "0.5",
        ["Redis:MinimumThroughput"] = "10",
        ["Redis:SamplingDurationSeconds"] = "30",
        ["Redis:BreakDurationSeconds"] = "15",
        ["Settings:Database:MaxRetryCount"] = "3",
        ["Settings:Database:MaxRetryDelaySeconds"] = "5",
        ["RabbitMq:HostName"] = rabbitMq.HostName,
        ["RabbitMq:Port"] = rabbitMq.Port.ToString(),
        ["RabbitMq:UserName"] = rabbitMq.UserName,
        ["RabbitMq:Password"] = rabbitMq.Password,
        ["RabbitMq:VirtualHost"] = "/",
        ["RabbitMq:ClientProvidedName"] = "pedeaqui-worker-tests",
        ["RabbitMq:ConnectionTimeoutSeconds"] = "2",
        ["RabbitMq:PublishTimeoutSeconds"] = "5",
        ["RabbitMq:NetworkRecoveryIntervalSeconds"] = "1",
        ["RabbitMq:PrefetchCount"] = "10",
        ["RabbitMq:RetryDelaysSeconds:0"] = RabbitMqTestBroker.RetryDelaysSeconds[0].ToString(),
        ["RabbitMq:RetryDelaysSeconds:1"] = RabbitMqTestBroker.RetryDelaysSeconds[1].ToString(),
        ["RabbitMq:RetryDelaysSeconds:2"] = RabbitMqTestBroker.RetryDelaysSeconds[2].ToString(),
        ["Outbox:BatchSize"] = batchSize.ToString(),
        ["Outbox:PollingIntervalMs"] = "100",
        ["Worker:ShutdownTimeoutSeconds"] = "30",
        ["Orders:PendingExpirationMinutes"] = "15",
        ["Orders:ExpirationBatchSize"] = "100",
        ["Payments:ReconcileAfterMinutes"] = "10",
        ["Payments:ReconcileBatchSize"] = "100",
        ["Jobs:ExpirationIntervalSeconds"] = "60",
        ["Jobs:ReconciliationIntervalSeconds"] = "60",
        ["Jobs:IdempotencyCleanupIntervalMinutes"] = "60",
        ["PaymentGateway:BaseUrl"] = "http://localhost:5000",
        ["AutoMapper:LicenseKey"] = ""
    };

    public static WorkerHarness Create(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddWorker(configuration);

        return new WorkerHarness(services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        }));
    }

    public async Task MigrateDatabase()
    {
        await using var scope = _services.CreateAsyncScope();
        await DatabaseMigration.Migrate(scope.ServiceProvider, CancellationToken.None);
    }

    public async Task<int> ProcessBatch()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxProcessor>().ProcessBatch(CancellationToken.None);
    }

    public async Task AddOutboxMessages(IEnumerable<OutboxMessage> messages)
    {
        await using var scope = _services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        dbContext.AddRange(messages);
        await dbContext.SaveChangesAsync();
    }

    public async Task<List<OutboxMessage>> OutboxMessages()
    {
        await using var scope = _services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        return await dbContext.Set<OutboxMessage>().AsNoTracking().ToListAsync();
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
