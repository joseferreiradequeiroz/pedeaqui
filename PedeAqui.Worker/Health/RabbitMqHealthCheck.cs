using Microsoft.Extensions.Diagnostics.HealthChecks;
using PedeAqui.Infrastructure.Messaging.RabbitMq;

namespace PedeAqui.Worker.Health;

public sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly RabbitMqConnection _connection;

    public RabbitMqHealthCheck(RabbitMqConnection connection)
    {
        _connection = connection;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connection = await _connection.Get(cancellationToken);

        return connection.IsOpen
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("RabbitMQ connection is closed");
    }
}
