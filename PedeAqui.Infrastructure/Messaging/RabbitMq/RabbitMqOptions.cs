namespace PedeAqui.Infrastructure.Messaging.RabbitMq;

public class RabbitMqOptions
{
    public const string SECTION = "RabbitMq";

    public string HostName { get; set; } = string.Empty;
    public int Port { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string VirtualHost { get; set; } = "/";
    public string ClientProvidedName { get; set; } = string.Empty;
    public int ConnectionTimeoutSeconds { get; set; }
    public int PublishTimeoutSeconds { get; set; }
    public int NetworkRecoveryIntervalSeconds { get; set; }
    public ushort PrefetchCount { get; set; }
    public int[] RetryDelaysSeconds { get; set; } = [];
}
