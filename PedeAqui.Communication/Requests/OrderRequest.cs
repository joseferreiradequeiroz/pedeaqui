namespace PedeAqui.Communication.Requests;

public class OrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = [];
}

public class OrderItemRequest
{
    public long ProductId { get; set; }
    public int Quantity { get; set; }
}
