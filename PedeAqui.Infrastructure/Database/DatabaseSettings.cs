namespace PedeAqui.Infrastructure.Database;

public class DatabaseSettings
{
    public const string SECTION = "Settings:Database";

    public int MaxRetryCount { get; set; }
    public int MaxRetryDelaySeconds { get; set; }
}
