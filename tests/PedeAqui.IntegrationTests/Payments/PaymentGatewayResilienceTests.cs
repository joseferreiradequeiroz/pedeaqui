using System.Diagnostics;
using System.Net;
using Polly.CircuitBreaker;
using Polly.Timeout;
using PedeAqui.Domain.Payments;

namespace PedeAqui.IntegrationTests.Payments;

public class PaymentGatewayResilienceTests
{
    private const string ORDER_REFERENCE = "pedido-teste";

    private static string NewIdempotencyKey() => $"order-{Guid.NewGuid()}";

    [Fact]
    public async Task Approved_payment_returns_approved()
    {
        await using var harness = await PaymentGatewayHarness.Start();

        var result = await harness.Gateway.Charge(NewIdempotencyKey(), ORDER_REFERENCE, 100.00m, CancellationToken.None);

        Assert.Equal(PaymentStatus.Approved, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.PaymentId));
        Assert.Equal(1, harness.Handler.Calls);
    }

    [Fact]
    public async Task Declined_payment_returns_declined()
    {
        await using var harness = await PaymentGatewayHarness.Start();

        var result = await harness.Gateway.Charge(NewIdempotencyKey(), ORDER_REFERENCE, 25.13m, CancellationToken.None);

        Assert.Equal(PaymentStatus.Declined, result.Status);
        Assert.Equal(1, harness.Handler.Calls);
    }

    [Fact]
    public async Task Slow_gateway_beyond_attempt_timeout_is_retried_then_fails_unavailable()
    {
        await using var harness = await PaymentGatewayHarness.Start();
        await harness.SetChaos(slowRate: 1, slowDelayMs: 5000);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() =>
            harness.Gateway.Charge(NewIdempotencyKey(), ORDER_REFERENCE, 100.00m, CancellationToken.None));

        stopwatch.Stop();
        Assert.IsType<TimeoutRejectedException>(exception.InnerException);
        Assert.Equal(PaymentGatewayHarness.MAX_RETRY_ATTEMPTS + 1, harness.Handler.Calls);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Levou {stopwatch.Elapsed}, o timeout por tentativa não cortou as chamadas lentas.");
    }

    [Fact]
    public async Task Transient_503_is_retried()
    {
        await using var harness = await PaymentGatewayHarness.Start(failFirstCalls: 2);

        var result = await harness.Gateway.Charge(NewIdempotencyKey(), ORDER_REFERENCE, 100.00m, CancellationToken.None);

        Assert.Equal(PaymentStatus.Approved, result.Status);
        Assert.Equal(3, harness.Handler.Calls);
    }

    [Fact]
    public async Task Consecutive_failures_open_circuit_and_stop_calling_gateway()
    {
        await using var harness = await PaymentGatewayHarness.Start();
        await harness.SetChaos(down: true);

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() =>
            harness.Gateway.Charge(NewIdempotencyKey(), ORDER_REFERENCE, 100.00m, CancellationToken.None));
        Assert.Equal(PaymentGatewayHarness.MAX_RETRY_ATTEMPTS + 1, harness.Handler.Calls);

        var openingFailure = await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() =>
            harness.Gateway.Charge(NewIdempotencyKey(), ORDER_REFERENCE, 100.00m, CancellationToken.None));
        Assert.IsType<BrokenCircuitException>(openingFailure.InnerException);
        var callsWhenCircuitOpened = harness.Handler.Calls;
        Assert.Equal(PaymentGatewayHarness.MINIMUM_THROUGHPUT, callsWhenCircuitOpened);

        for (var call = 0; call < 3; call++)
        {
            var exception = await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() =>
                harness.Gateway.Charge(NewIdempotencyKey(), ORDER_REFERENCE, 100.00m, CancellationToken.None));
            Assert.IsType<BrokenCircuitException>(exception.InnerException);
        }

        Assert.Equal(callsWhenCircuitOpened, harness.Handler.Calls);
    }

    [Fact]
    public async Task Client_error_4xx_is_not_retried()
    {
        await using var harness = await PaymentGatewayHarness.Start();
        var idempotencyKey = NewIdempotencyKey();
        await harness.Gateway.Charge(idempotencyKey, ORDER_REFERENCE, 100.00m, CancellationToken.None);
        var callsBefore = harness.Handler.Calls;

        var exception = await Assert.ThrowsAsync<PaymentGatewayRejectedException>(() =>
            harness.Gateway.Charge(idempotencyKey, ORDER_REFERENCE, 200.00m, CancellationToken.None));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.GatewayStatusCode);
        Assert.Equal(1, harness.Handler.Calls - callsBefore);
    }

    [Fact]
    public async Task Client_errors_do_not_open_the_circuit()
    {
        await using var harness = await PaymentGatewayHarness.Start();

        for (var call = 0; call < PaymentGatewayHarness.MINIMUM_THROUGHPUT * 2; call++)
        {
            await Assert.ThrowsAsync<PaymentGatewayRejectedException>(() =>
                harness.Gateway.Charge(NewIdempotencyKey(), ORDER_REFERENCE, 0m, CancellationToken.None));
        }

        var result = await harness.Gateway.Charge(NewIdempotencyKey(), ORDER_REFERENCE, 100.00m, CancellationToken.None);

        Assert.Equal(PaymentStatus.Approved, result.Status);
        Assert.Equal(PaymentGatewayHarness.MINIMUM_THROUGHPUT * 2 + 1, harness.Handler.Calls);
    }

    [Fact]
    public async Task Retries_reuse_the_same_idempotency_key()
    {
        await using var harness = await PaymentGatewayHarness.Start(failFirstCalls: 2);
        var idempotencyKey = NewIdempotencyKey();

        await harness.Gateway.Charge(idempotencyKey, ORDER_REFERENCE, 100.00m, CancellationToken.None);

        var attempts = harness.Handler.Attempts.ToArray();
        Assert.Equal(3, attempts.Length);
        Assert.All(attempts, attempt => Assert.Equal(idempotencyKey, attempt.IdempotencyKey));
    }

    [Fact]
    public async Task Every_attempt_carries_the_correlation_id()
    {
        await using var harness = await PaymentGatewayHarness.Start(failFirstCalls: 1);

        await harness.Gateway.Charge(NewIdempotencyKey(), ORDER_REFERENCE, 100.00m, CancellationToken.None);

        var attempts = harness.Handler.Attempts.ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.All(attempts, attempt => Assert.Equal(FixedCorrelationContext.CORRELATION_ID, attempt.CorrelationId));
    }

    [Fact]
    public async Task Find_returns_null_when_gateway_never_received_the_key()
    {
        await using var harness = await PaymentGatewayHarness.Start();

        var result = await harness.Gateway.Find(NewIdempotencyKey(), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, harness.Handler.Calls);
    }

    [Fact]
    public async Task Find_returns_the_payment_already_charged()
    {
        await using var harness = await PaymentGatewayHarness.Start();
        var idempotencyKey = NewIdempotencyKey();
        var charged = await harness.Gateway.Charge(idempotencyKey, ORDER_REFERENCE, 25.13m, CancellationToken.None);

        var found = await harness.Gateway.Find(idempotencyKey, CancellationToken.None);

        Assert.Equal(charged, found);
    }
}
