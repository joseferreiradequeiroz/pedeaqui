using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace PedeAqui.Worker.Health;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddWorkerHealthLogging(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgres", HealthStatus.Unhealthy)
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", HealthStatus.Unhealthy);

        services.AddSingleton<IHealthCheckPublisher, LoggingHealthCheckPublisher>();
        services.AddOptions<HealthCheckPublisherOptions>().Configure<IOptions<WorkerOptions>>((publisherOptions, workerOptions) =>
        {
            publisherOptions.Delay = TimeSpan.FromSeconds(workerOptions.Value.HealthCheckDelaySeconds);
            publisherOptions.Period = TimeSpan.FromSeconds(workerOptions.Value.HealthCheckIntervalSeconds);
            publisherOptions.Timeout = TimeSpan.FromSeconds(workerOptions.Value.HealthCheckTimeoutSeconds);
        });

        return services;
    }
}
