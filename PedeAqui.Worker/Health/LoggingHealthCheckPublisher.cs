using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PedeAqui.Worker.Health;

public sealed class LoggingHealthCheckPublisher : IHealthCheckPublisher
{
    private readonly ILogger<LoggingHealthCheckPublisher> _logger;

    public LoggingHealthCheckPublisher(ILogger<LoggingHealthCheckPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        var level = report.Status == HealthStatus.Healthy ? LogLevel.Information : LogLevel.Warning;
        var unhealthyChecks = report.Entries
            .Where(entry => entry.Value.Status != HealthStatus.Healthy)
            .Select(entry => entry.Key)
            .ToArray();

        _logger.Log(level, "Worker health is {Status}. Failing checks: {FailingChecks}", report.Status, unhealthyChecks);
        return Task.CompletedTask;
    }
}
