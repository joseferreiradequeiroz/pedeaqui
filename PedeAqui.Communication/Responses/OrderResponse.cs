using PedeAqui.Communication.Enums;

namespace PedeAqui.Communication.Responses;

public class OrderResponse
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<OrderItemResponse> Items { get; set; } = [];
}

public class OrderItemResponse
{
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Subtotal { get; set; }
}

public class ShortOrderResponse
{
    public long Id { get; set; }
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OrdersResponse
{
    public List<ShortOrderResponse> Orders { get; set; } = [];
}
