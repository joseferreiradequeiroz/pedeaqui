using Microsoft.Extensions.Options;
using PedeAqui.Application.Settings;
using PedeAqui.Infrastructure.Messaging.RabbitMq;
using PedeAqui.Infrastructure.Payments;

namespace PedeAqui.Worker;

public sealed class ReconciliationWindowValidator : IValidateOptions<PaymentsOptions>
{
    private const int SECONDS_PER_MINUTE = 60;

    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly PaymentGatewayOptions _paymentGatewayOptions;

    public ReconciliationWindowValidator(IOptions<RabbitMqOptions> rabbitMqOptions, IOptions<PaymentGatewayOptions> paymentGatewayOptions)
    {
        _rabbitMqOptions = rabbitMqOptions.Value;
        _paymentGatewayOptions = paymentGatewayOptions.Value;
    }

    public ValidateOptionsResult Validate(string? name, PaymentsOptions options)
    {
        var reconciliationWindowSeconds = (double)options.ReconcileAfterMinutes * SECONDS_PER_MINUTE;
        var deliveryAttempts = _rabbitMqOptions.RetryDelaysSeconds.Length + 1;
        var retryBudgetSeconds = _rabbitMqOptions.RetryDelaysSeconds.Sum() + deliveryAttempts * _paymentGatewayOptions.TotalTimeoutSeconds;

        if (reconciliationWindowSeconds > retryBudgetSeconds)
            return ValidateOptionsResult.Success;

        return ValidateOptionsResult.Fail(
            $"{PaymentsOptions.SECTION}:{nameof(PaymentsOptions.ReconcileAfterMinutes)} ({reconciliationWindowSeconds} s) must exceed " +
            $"the sum of {RabbitMqOptions.SECTION}:{nameof(RabbitMqOptions.RetryDelaysSeconds)} plus {deliveryAttempts} x " +
            $"{PaymentGatewayOptions.SECTION}:{nameof(PaymentGatewayOptions.TotalTimeoutSeconds)} ({retryBudgetSeconds} s), " +
            "otherwise the reconciler may cancel an order whose charge is still being retried");
    }
}
