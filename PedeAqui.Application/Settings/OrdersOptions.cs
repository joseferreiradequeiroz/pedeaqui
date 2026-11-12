namespace PedeAqui.Application.Settings;

public class OrdersOptions
{
    public const string SECTION = "Orders";

    public int PendingExpirationMinutes { get; set; }
    public int ExpirationBatchSize { get; set; }
}
