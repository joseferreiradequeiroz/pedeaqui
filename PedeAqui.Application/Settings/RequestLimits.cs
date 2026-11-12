namespace PedeAqui.Application.Settings;

public class RequestLimits
{
    public const string SECTION = "RequestLimits";

    public int MaxOrderItems { get; set; }
    public int MaxItemQuantity { get; set; }
}
