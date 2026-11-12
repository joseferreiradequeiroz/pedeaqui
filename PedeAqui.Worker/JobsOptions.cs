namespace PedeAqui.Worker;

public class JobsOptions
{
    public const string SECTION = "Jobs";

    public int ExpirationIntervalSeconds { get; set; }
    public int ReconciliationIntervalSeconds { get; set; }
    public int IdempotencyCleanupIntervalMinutes { get; set; }
}
