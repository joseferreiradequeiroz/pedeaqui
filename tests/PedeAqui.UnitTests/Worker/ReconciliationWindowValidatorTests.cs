using Microsoft.Extensions.Options;
using PedeAqui.Application.Settings;
using PedeAqui.Infrastructure.Messaging.RabbitMq;
using PedeAqui.Infrastructure.Payments;
using PedeAqui.Worker;

namespace PedeAqui.UnitTests.Worker;

public class ReconciliationWindowValidatorTests
{
    private static readonly int[] RetryDelaysSeconds = [5, 30, 120];
    private const double GATEWAY_TOTAL_TIMEOUT_SECONDS = 10;

    [Fact]
    public void Window_longer_than_every_delivery_attempt_is_valid()
    {
        var result = Validate(reconcileAfterMinutes: 10);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Window_shorter_than_the_retry_budget_fails()
    {
        var result = Validate(reconcileAfterMinutes: 3);

        Assert.True(result.Failed);
        Assert.Contains("Payments:ReconcileAfterMinutes", result.FailureMessage);
    }

    [Fact]
    public void Window_exactly_equal_to_the_retry_budget_fails()
    {
        var result = Validate(reconcileAfterMinutes: 1, retryDelaysSeconds: [40]);

        Assert.True(result.Failed);
    }

    private static ValidateOptionsResult Validate(int reconcileAfterMinutes, int[]? retryDelaysSeconds = null)
    {
        var validator = new ReconciliationWindowValidator(
            Options.Create(new RabbitMqOptions { RetryDelaysSeconds = retryDelaysSeconds ?? RetryDelaysSeconds }),
            Options.Create(new PaymentGatewayOptions { TotalTimeoutSeconds = GATEWAY_TOTAL_TIMEOUT_SECONDS }));

        return validator.Validate(null, new PaymentsOptions { ReconcileAfterMinutes = reconcileAfterMinutes });
    }
}
