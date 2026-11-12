using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PedeAqui.Infrastructure.Messaging.Outbox;
using PedeAqui.UnitTests.Fakes;
using PedeAqui.Worker.HostedServices;

namespace PedeAqui.UnitTests.Worker;

public class OutboxPublisherServiceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Keeps_polling_after_a_cycle_throws()
    {
        var processor = new FakeOutboxProcessor(() => throw new InvalidOperationException("broker down"), () => 0);
        using var service = ServiceWith(processor, batchSize: 50);

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => processor.CallCount >= 3);
        await service.StopAsync(CancellationToken.None);

        Assert.True(processor.CallCount >= 3);
    }

    [Fact]
    public async Task Processes_next_batch_immediately_when_batch_was_full()
    {
        var processor = new FakeOutboxProcessor(() => 2, () => 2, () => 1);
        using var service = ServiceWith(processor, batchSize: 2, pollingIntervalMs: 60_000);

        await service.StartAsync(CancellationToken.None);
        await WaitUntil(() => processor.CallCount >= 3);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(3, processor.CallCount);
    }

    private static OutboxPublisherService ServiceWith(FakeOutboxProcessor processor, int batchSize, int pollingIntervalMs = 10)
    {
        var services = new ServiceCollection();
        services.AddScoped<IOutboxProcessor>(_ => processor);
        var provider = services.BuildServiceProvider();

        return new OutboxPublisherService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxOptions { BatchSize = batchSize, PollingIntervalMs = pollingIntervalMs }),
            NullLogger<OutboxPublisherService>.Instance);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }
}
