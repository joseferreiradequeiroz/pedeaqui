namespace PedeAqui.Infrastructure.Payments;

public class PaymentGatewayOptions
{
    public const string SECTION = "PaymentGateway";

    public string BaseUrl { get; set; } = string.Empty;
    public double TotalTimeoutSeconds { get; set; }
    public double AttemptTimeoutSeconds { get; set; }
    public PaymentGatewayBulkheadOptions Bulkhead { get; set; } = new();
    public PaymentGatewayRetryOptions Retry { get; set; } = new();
    public PaymentGatewayCircuitBreakerOptions CircuitBreaker { get; set; } = new();
}

public class PaymentGatewayBulkheadOptions
{
    public int MaxConcurrency { get; set; }
    public int QueueLimit { get; set; }
}

public class PaymentGatewayRetryOptions
{
    public int MaxRetryAttempts { get; set; }
    public int DelayMilliseconds { get; set; }
}

public class PaymentGatewayCircuitBreakerOptions
{
    public double FailureRatio { get; set; }
    public int MinimumThroughput { get; set; }
    public double SamplingDurationSeconds { get; set; }
    public double BreakDurationSeconds { get; set; }
}
