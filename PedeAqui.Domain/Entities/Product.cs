using PedeAqui.Domain.Enums;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Domain.Entities;

public class Product
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool Active { get; private set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int StockQuantity { get; private set; }

    public StockMovement AddStock(int quantity, string reason, Order? order = null)
    {
        EnsurePositive(quantity);

        StockQuantity += quantity;

        return StockMovement.Create(this, StockMovementType.Entry, quantity, reason, order);
    }

    public StockMovement RemoveStock(int quantity, string reason, Order? order = null)
    {
        EnsurePositive(quantity);

        if (quantity > StockQuantity)
            throw new BusinessRuleException(
                $"{ResourceErrorMessages.INSUFFICIENT_STOCK} Produto '{Name}': disponível {StockQuantity}, solicitado {quantity}.");

        StockQuantity -= quantity;

        return StockMovement.Create(this, StockMovementType.Exit, quantity, reason, order);
    }

    public void Deactivate() => Active = false;

    public void EnsureCanBeSold()
    {
        if (!Active)
            throw new BusinessRuleException($"{ResourceErrorMessages.PRODUCT_INACTIVE} Produto '{Name}'.");
    }

    private static void EnsurePositive(int quantity)
    {
        if (quantity <= 0)
            throw new BusinessRuleException(ResourceErrorMessages.STOCK_QUANTITY_MUST_BE_GREATER_THAN_ZERO);
    }
}
