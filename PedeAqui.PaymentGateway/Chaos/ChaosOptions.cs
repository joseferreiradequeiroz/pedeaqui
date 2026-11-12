namespace PedeAqui.PaymentGateway.Chaos;

public record ChaosOptions
{
    public const string SectionName = "Chaos";

    public double FailureRate { get; init; }
    public double SlowRate { get; init; }
    public int SlowDelayMs { get; init; }
    public bool Down { get; init; }

    public bool IsValid() =>
        FailureRate is >= 0 and <= 1 &&
        SlowRate is >= 0 and <= 1 &&
        SlowDelayMs >= 0;
}
