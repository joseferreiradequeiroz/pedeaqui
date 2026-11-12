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
    public DateTime? PaymentStartedAt { get; private set; }
    public bool IsFinal => Status is OrderStatus.Confirmed or OrderStatus.Cancelled or OrderStatus.Expired;
    public List<OrderItem> Items { get; set; } = [];

    public void SetItem(Product product, int quantity)
    {
        EnsureCanBeChanged();

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
        EnsureCanBeChanged();

        Items.RemoveAll(item => item.ProductId == productId);
        RecalculateTotal();
    }

    public void StartPayment()
    {
        MoveTo(OrderStatus.AwaitingPayment, OrderStatus.Pending);
        PaymentStartedAt = DateTime.UtcNow;
    }

    public void ConfirmPayment()
    {
        MoveTo(OrderStatus.Confirmed, OrderStatus.AwaitingPayment);
    }

    public void FailPayment()
    {
        MoveTo(OrderStatus.PaymentFailed, OrderStatus.AwaitingPayment);
        MoveTo(OrderStatus.Cancelled, OrderStatus.PaymentFailed);
    }

    public void Cancel()
    {
        MoveTo(OrderStatus.Cancelled, OrderStatus.Pending);
    }

    public void Expire()
    {
        MoveTo(OrderStatus.Expired, OrderStatus.Pending);
    }

    public void EnsureCanBeChanged()
    {
        if (Status != OrderStatus.Pending)
            throw new ConflictException(ResourceErrorMessages.ORDER_CANNOT_BE_CHANGED);
    }

    private void MoveTo(OrderStatus destination, OrderStatus requiredOrigin)
    {
        if (Status != requiredOrigin)
            throw new ConflictException(string.Format(ResourceErrorMessages.ORDER_INVALID_STATUS_TRANSITION, Status, destination));

        Status = destination;
        UpdatedAt = DateTime.UtcNow;
    }

    private void RecalculateTotal()
    {
        Total = Items.Sum(item => item.Subtotal);
        UpdatedAt = DateTime.UtcNow;
    }
}
