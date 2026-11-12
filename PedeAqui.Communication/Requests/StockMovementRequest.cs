namespace PedeAqui.Communication.Requests;

public class StockMovementRequest
{
    public int Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}
