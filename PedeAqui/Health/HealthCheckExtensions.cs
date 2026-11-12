using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PedeAqui.Infrastructure.Messaging.RabbitMq;
using PedeAqui.Settings;

namespace PedeAqui.Health;

public static class HealthCheckExtensions
{
    private const string READY_TAG = "ready";
    private const string LIVE_PATH = "/health/live";
    private const string READY_PATH = "/health/ready";

    public static IServiceCollection AddPedeAquiHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var timeout = TimeSpan.FromSeconds(configuration.GetSection(HealthSettings.SECTION).Get<HealthSettings>()!.CheckTimeoutSeconds);
        var readyTags = new[] { READY_TAG };

        services.Configure<RabbitMqOptions>(options => configuration.GetSection(RabbitMqOptions.SECTION).Bind(options));
        services.TryAddSingleton<RabbitMqConnection>();

        services.AddHealthChecks()
            .AddNpgSql(
                configuration.GetConnectionString("Connection")!,
                name: "postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: readyTags,
                timeout: timeout)
            .AddRedis(
                configuration.GetConnectionString("Redis")!,
                name: "redis",
                failureStatus: HealthStatus.Degraded,
                tags: readyTags,
                timeout: timeout)
            .AddRabbitMQ(
                serviceProvider => serviceProvider.GetRequiredService<RabbitMqConnection>().Get(CancellationToken.None),
                name: "rabbitmq",
                failureStatus: HealthStatus.Unhealthy,
                tags: readyTags,
                timeout: timeout);

        return services;
    }

    public static IEndpointRouteBuilder MapPedeAquiHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LIVE_PATH, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthResponseWriter.Write
        });

        endpoints.MapHealthChecks(READY_PATH, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(READY_TAG),
            ResponseWriter = HealthResponseWriter.Write
        });

        return endpoints;
    }
}
