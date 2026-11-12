using Microsoft.Extensions.Options;
using PedeAqui.Infrastructure.Messaging.Outbox;

namespace PedeAqui.Worker.HostedServices;

public sealed class OutboxPublisherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxPublisherService> _logger;

    public OutboxPublisherService(IServiceScopeFactory scopeFactory, IOptions<OutboxOptions> options, ILogger<OutboxPublisherService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox publisher started (batch size {BatchSize}, polling every {PollingIntervalMs} ms)",
            _options.BatchSize, _options.PollingIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            var published = await RunCycle(stoppingToken);

            if (published >= _options.BatchSize)
                continue;

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_options.PollingIntervalMs), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Outbox publisher stopped");
    }

    private async Task<int> RunCycle(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
            return await processor.ProcessBatch(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (System.Exception exception)
        {
            _logger.LogError(exception, "Outbox publishing cycle failed; retrying on the next cycle");
            return 0;
        }
    }
}
