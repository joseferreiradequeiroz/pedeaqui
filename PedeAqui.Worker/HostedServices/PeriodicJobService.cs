using PedeAqui.Application.UseCases;

namespace PedeAqui.Worker.HostedServices;

public abstract class PeriodicJobService<TUseCase> : BackgroundService where TUseCase : IBackgroundJobUseCase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private readonly string _jobName;
    private readonly TimeSpan _interval;

    protected PeriodicJobService(IServiceScopeFactory scopeFactory, ILogger logger, string jobName, TimeSpan interval)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _jobName = jobName;
        _interval = interval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Job {JobName} started (running every {Interval})", _jobName, _interval);

        using var timer = new PeriodicTimer(_interval);

        try
        {
            do
            {
                await RunCycle(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        _logger.LogInformation("Job {JobName} stopped", _jobName);
    }

    private async Task RunCycle(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var useCase = scope.ServiceProvider.GetRequiredService<TUseCase>();
            var affected = await useCase.Execute(stoppingToken);

            if (affected > 0)
                _logger.LogInformation("Job {JobName} handled {Count} item(s)", _jobName, affected);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (System.Exception exception)
        {
            _logger.LogError(exception, "Job {JobName} cycle failed; retrying on the next cycle", _jobName);
        }
    }
}
