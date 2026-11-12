using Microsoft.Extensions.Diagnostics.HealthChecks;
using PedeAqui.Infrastructure.Database;

namespace PedeAqui.Worker.Health;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        return await DatabaseConnectivity.CanConnect(scope.ServiceProvider, cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("PostgreSQL is unreachable");
    }
}
