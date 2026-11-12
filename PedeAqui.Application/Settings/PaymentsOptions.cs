namespace PedeAqui.Application.Settings;

public class PaymentsOptions
{
    public const string SECTION = "Payments";

    public int ReconcileAfterMinutes { get; set; }
    public int ReconcileBatchSize { get; set; }
}
