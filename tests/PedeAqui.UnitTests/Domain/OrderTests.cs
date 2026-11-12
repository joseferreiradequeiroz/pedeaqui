using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;
using PedeAqui.UnitTests.Builders;

namespace PedeAqui.UnitTests.Domain;

public class OrderTests
{
    [Fact]
    public void NewOrder_StartsPendingWithZeroTotal()
    {
        var order = new Order();

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(0, order.Total);
        Assert.Empty(order.Items);
    }

    [Fact]
    public void SetItem_NewProduct_AddsItemWithCurrentPriceAndRecalculatesTotal()
    {
        var order = new Order();
        var pizza = ProductBuilder.Build(id: 1, price: 50m);
        var soda = ProductBuilder.Build(id: 2, price: 8.5m);

        order.SetItem(pizza, 2);
        order.SetItem(soda, 3);

        Assert.Equal(2, order.Items.Count);
        Assert.Equal(125.5m, order.Total);
        var item = order.Items.Single(i => i.ProductId == pizza.Id);
        Assert.Equal(pizza.Name, item.ProductName);
        Assert.Equal(50m, item.UnitPrice);
    }

    [Fact]
    public void SetItem_ExistingProduct_ChangesQuantityAndKeepsOriginalPrice()
    {
        var order = new Order();
        var pizza = ProductBuilder.Build(price: 50m);
        order.SetItem(pizza, 1);
        pizza.Price = 70m;

        order.SetItem(pizza, 3);

        var item = Assert.Single(order.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(50m, item.UnitPrice);
        Assert.Equal(150m, order.Total);
    }

    [Fact]
    public void RemoveItem_ExistingProduct_RemovesAndRecalculatesTotal()
    {
        var order = new Order();
        var pizza = ProductBuilder.Build(id: 1, price: 50m);
        var soda = ProductBuilder.Build(id: 2, price: 10m);
        order.SetItem(pizza, 1);
        order.SetItem(soda, 1);

        order.RemoveItem(pizza.Id);

        var item = Assert.Single(order.Items);
        Assert.Equal(soda.Id, item.ProductId);
        Assert.Equal(10m, order.Total);
    }

    [Fact]
    public void Confirm_PendingOrder_ChangesStatus()
    {
        var order = new Order();

        order.Confirm();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.NotNull(order.UpdatedAt);
    }

    [Fact]
    public void Cancel_PendingOrder_ChangesStatus()
    {
        var order = new Order();

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_ConfirmedOrder_Throws()
    {
        var order = new Order();
        order.Confirm();

        var exception = Assert.Throws<BusinessRuleException>(order.Cancel);

        Assert.Equal(ResourceErrorMessages.ORDER_CANNOT_BE_CANCELLED, exception.Message);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Confirm_CancelledOrder_Throws()
    {
        var order = new Order();
        order.Cancel();

        var exception = Assert.Throws<BusinessRuleException>(order.Confirm);

        Assert.Equal(ResourceErrorMessages.ORDER_CANNOT_BE_CONFIRMED, exception.Message);
    }

    [Fact]
    public void SetItem_CancelledOrder_Throws()
    {
        var order = new Order();
        order.Cancel();

        var exception = Assert.Throws<BusinessRuleException>(() => order.SetItem(ProductBuilder.Build(), 1));

        Assert.Equal(ResourceErrorMessages.ORDER_CANNOT_BE_CHANGED, exception.Message);
        Assert.Empty(order.Items);
    }
}
