using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

namespace PedeAqui.Infrastructure.Payments;

public static class PaymentGatewayResilience
{
    public const string CLIENT_NAME = "payment-gateway";
    public const string PIPELINE_NAME = "payment-gateway";

    public static IHttpClientBuilder AddPaymentGatewayResilience(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler(PIPELINE_NAME, (pipeline, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<PaymentGatewayOptions>>().Value;
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PaymentGatewayResilience).FullName!);

            pipeline
                .AddConcurrencyLimiter(options.Bulkhead.MaxConcurrency, options.Bulkhead.QueueLimit)
                .AddTimeout(TimeSpan.FromSeconds(options.TotalTimeoutSeconds))
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.Retry.MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(options.Retry.DelayMilliseconds),
                    ShouldHandle = arguments => ValueTask.FromResult(IsTransientFailure(arguments.Outcome)),
                    OnRetry = arguments =>
                    {
                        logger.LogWarning(
                            "Gateway de pagamento falhou de forma transitória ({Failure}). Nova tentativa {Attempt} em {Delay}.",
                            DescribeFailure(arguments.Outcome),
                            arguments.AttemptNumber + 1,
                            arguments.RetryDelay);
                        return default;
                    }
                })
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = options.CircuitBreaker.FailureRatio,
                    MinimumThroughput = options.CircuitBreaker.MinimumThroughput,
                    SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreaker.SamplingDurationSeconds),
                    BreakDuration = TimeSpan.FromSeconds(options.CircuitBreaker.BreakDurationSeconds),
                    ShouldHandle = arguments => ValueTask.FromResult(IsTransientFailure(arguments.Outcome)),
                    OnOpened = arguments =>
                    {
                        logger.LogWarning("Circuito do gateway de pagamento aberto por {BreakDuration}. Chamadas serão recusadas sem tocar no gateway.", arguments.BreakDuration);
                        return default;
                    },
                    OnHalfOpened = _ =>
                    {
                        logger.LogWarning("Circuito do gateway de pagamento meio aberto. Uma chamada de teste vai passar.");
                        return default;
                    },
                    OnClosed = _ =>
                    {
                        logger.LogWarning("Circuito do gateway de pagamento fechado. O gateway voltou a responder.");
                        return default;
                    }
                })
                .AddTimeout(TimeSpan.FromSeconds(options.AttemptTimeoutSeconds));
        });

        return builder;
    }

    public static bool IsTransientFailure(Outcome<HttpResponseMessage> outcome) => outcome switch
    {
        { Exception: HttpRequestException } => true,
        { Exception: TimeoutRejectedException } => true,
        { Result: { } response } => IsTransientStatusCode(response.StatusCode),
        _ => false
    };

    public static bool IsTransientStatusCode(HttpStatusCode statusCode) =>
        (int)statusCode >= 500 || statusCode == HttpStatusCode.RequestTimeout;

    private static string DescribeFailure(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception?.GetType().Name ?? $"HTTP {(int)outcome.Result!.StatusCode}";
}
