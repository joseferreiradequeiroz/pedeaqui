namespace PedeAqui.Infrastructure.Messaging.Outbox;

public class OutboxOptions
{
    public const string SECTION = "Outbox";

    public int BatchSize { get; set; }
    public int PollingIntervalMs { get; set; }
}
