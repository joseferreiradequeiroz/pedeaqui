using PedeAqui.Domain.Entities;

namespace PedeAqui.UnitTests.Support;

public static class OrderFactory
{
    public static Order PendingWith(User owner, params (Product product, int quantity)[] items)
    {
        var order = new Order { UserId = owner.Id };

        foreach (var (product, quantity) in items)
        {
            product.RemoveStock(quantity, "Venda", order);
            order.SetItem(product, quantity);
        }

        return order;
    }
}
