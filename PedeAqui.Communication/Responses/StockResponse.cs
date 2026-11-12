using PedeAqui.Communication.Enums;

namespace PedeAqui.Communication.Responses;

public class StockResponse
{
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public List<StockMovementResponse> Movements { get; set; } = [];
}

public class StockMovementResponse
{
    public long Id { get; set; }
    public StockMovementType Type { get; set; }
    public int Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long? OrderId { get; set; }
    public DateTime CreatedAt { get; set; }
}
