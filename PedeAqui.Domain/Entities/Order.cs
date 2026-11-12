using PedeAqui.Domain.Enums;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Domain.Entities;

public class Order
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = default!;
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public decimal Total { get; private set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }
    public List<OrderItem> Items { get; set; } = [];

    public void SetItem(Product product, int quantity)
    {
        EnsureIsPending(ResourceErrorMessages.ORDER_CANNOT_BE_CHANGED);

        var item = Items.FirstOrDefault(item => item.ProductId == product.Id);
        if (item is null)
        {
            Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = quantity
            });
        }
        else
        {
            item.Quantity = quantity;
        }

        RecalculateTotal();
    }

    public void RemoveItem(long productId)
    {
        EnsureIsPending(ResourceErrorMessages.ORDER_CANNOT_BE_CHANGED);

        Items.RemoveAll(item => item.ProductId == productId);
        RecalculateTotal();
    }

    public void Confirm()
    {
        EnsureIsPending(ResourceErrorMessages.ORDER_CANNOT_BE_CONFIRMED);
        Status = OrderStatus.Confirmed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        EnsureIsPending(ResourceErrorMessages.ORDER_CANNOT_BE_CANCELLED);
        Status = OrderStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }

    public void EnsureIsPending(string message)
    {
        if (Status != OrderStatus.Pending)
            throw new BusinessRuleException(message);
    }

    private void RecalculateTotal()
    {
        Total = Items.Sum(item => item.Subtotal);
        UpdatedAt = DateTime.UtcNow;
    }
}
