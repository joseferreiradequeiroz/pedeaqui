using System.Globalization;

namespace PedeAqui.IntegrationTests.Payments;

public static class PaymentGatewayTestSettings
{
    public static void Apply(Dictionary<string, string?> settings, string baseUrl)
    {
        settings["PaymentGateway:BaseUrl"] = baseUrl;
        settings["PaymentGateway:Bulkhead:MaxConcurrency"] = "10";
        settings["PaymentGateway:Bulkhead:QueueLimit"] = "20";
        settings["PaymentGateway:TotalTimeoutSeconds"] = "5";
        settings["PaymentGateway:Retry:MaxRetryAttempts"] = "3";
        settings["PaymentGateway:Retry:DelayMilliseconds"] = "10";
        settings["PaymentGateway:CircuitBreaker:FailureRatio"] = "0.5";
        settings["PaymentGateway:CircuitBreaker:MinimumThroughput"] = "5";
        settings["PaymentGateway:CircuitBreaker:SamplingDurationSeconds"] = "30";
        settings["PaymentGateway:CircuitBreaker:BreakDurationSeconds"] = "30";
        settings["PaymentGateway:AttemptTimeoutSeconds"] = 0.5.ToString(CultureInfo.InvariantCulture);
    }
}
