using PedeAqui.Domain.Enums;

namespace PedeAqui.Domain.Entities;

public class StockMovement
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public Product Product { get; set; } = default!;
    public StockMovementType Type { get; set; }
    public int Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long? OrderId { get; set; }
    public Order? Order { get; set; }
    public DateTime CreatedAt { get; set; }

    internal static StockMovement Create(Product product, StockMovementType type, int quantity, string reason, Order? order)
    {
        return new StockMovement
        {
            ProductId = product.Id,
            Product = product,
            Type = type,
            Quantity = quantity,
            Reason = reason,
            OrderId = order?.Id,
            Order = order,
            CreatedAt = DateTime.UtcNow
        };
    }
}
