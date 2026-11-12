namespace PedeAqui.Worker;

public class WorkerOptions
{
    public const string SECTION = "Worker";

    public int ShutdownTimeoutSeconds { get; set; }
    public int HealthCheckDelaySeconds { get; set; }
    public int HealthCheckIntervalSeconds { get; set; }
    public int HealthCheckTimeoutSeconds { get; set; }
}
