using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;

namespace PedeAqui.Infrastructure.Redis;

public static class RedisResilience
{
    public const string PIPELINE_NAME = "redis";

    public static IServiceCollection AddRedisResilience(this IServiceCollection services)
    {
        services.AddResiliencePipeline(PIPELINE_NAME, (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<RedisOptions>>().Value;
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RedisResilience).FullName!);

            builder
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = options.FailureRatio,
                    MinimumThroughput = options.MinimumThroughput,
                    SamplingDuration = TimeSpan.FromSeconds(options.SamplingDurationSeconds),
                    BreakDuration = TimeSpan.FromSeconds(options.BreakDurationSeconds),
                    OnOpened = arguments =>
                    {
                        logger.LogWarning("Circuito do Redis aberto por {BreakDuration}. Seguindo sem Redis.", arguments.BreakDuration);
                        return default;
                    },
                    OnClosed = _ =>
                    {
                        logger.LogWarning("Circuito do Redis fechado. Redis voltou a ser usado.");
                        return default;
                    }
                })
                .AddTimeout(TimeSpan.FromMilliseconds(options.TimeoutMilliseconds));
        });

        return services;
    }
}
