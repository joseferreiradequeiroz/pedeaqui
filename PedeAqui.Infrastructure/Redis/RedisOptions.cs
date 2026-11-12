namespace PedeAqui.Infrastructure.Redis;

public class RedisOptions
{
    public const string SECTION = "Redis";

    public string InstanceName { get; set; } = string.Empty;
    public int ProductCacheTtlSeconds { get; set; }
    public int TimeoutMilliseconds { get; set; }
    public double FailureRatio { get; set; }
    public int MinimumThroughput { get; set; }
    public int SamplingDurationSeconds { get; set; }
    public int BreakDurationSeconds { get; set; }
}
