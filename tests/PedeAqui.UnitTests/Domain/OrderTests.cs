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
    public void Cancel_PendingOrder_ChangesStatus()
    {
        var order = new Order();

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.NotNull(order.UpdatedAt);
    }

    [Fact]
    public void Expire_PendingOrder_ChangesStatus()
    {
        var order = new Order();

        order.Expire();

        Assert.Equal(OrderStatus.Expired, order.Status);
        Assert.NotNull(order.UpdatedAt);
    }

    [Fact]
    public void StartPayment_PendingOrder_MovesToAwaitingPaymentAndStampsPaymentStart()
    {
        var order = new Order();

        order.StartPayment();

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.NotNull(order.PaymentStartedAt);
    }

    [Fact]
    public void ConfirmPayment_AwaitingPaymentOrder_Confirms()
    {
        var order = new Order();
        order.StartPayment();

        order.ConfirmPayment();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void FailPayment_AwaitingPaymentOrder_EndsCancelled()
    {
        var order = new Order();
        order.StartPayment();

        order.FailPayment();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void ConfirmPayment_requires_AwaitingPayment()
    {
        var order = new Order();

        var exception = Assert.Throws<ConflictException>(order.ConfirmPayment);

        Assert.Equal(TransitionMessage(OrderStatus.Pending, OrderStatus.Confirmed), exception.Message);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void FailPayment_requires_AwaitingPayment()
    {
        var order = new Order();

        Assert.Throws<ConflictException>(order.FailPayment);

        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void Cancel_from_AwaitingPayment_throws_conflict()
    {
        var order = new Order();
        order.StartPayment();

        var exception = Assert.Throws<ConflictException>(order.Cancel);

        Assert.Equal(TransitionMessage(OrderStatus.AwaitingPayment, OrderStatus.Cancelled), exception.Message);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void Expire_from_AwaitingPayment_throws_conflict()
    {
        var order = new Order();
        order.StartPayment();

        var exception = Assert.Throws<ConflictException>(order.Expire);

        Assert.Equal(TransitionMessage(OrderStatus.AwaitingPayment, OrderStatus.Expired), exception.Message);
    }

    [Fact]
    public void StartPayment_from_Cancelled_throws_conflict()
    {
        var order = new Order();
        order.Cancel();

        var exception = Assert.Throws<ConflictException>(order.StartPayment);

        Assert.Equal(TransitionMessage(OrderStatus.Cancelled, OrderStatus.AwaitingPayment), exception.Message);
        Assert.Null(order.PaymentStartedAt);
    }

    [Fact]
    public void Cancel_from_Cancelled_throws_conflict()
    {
        var order = new Order();
        order.Cancel();

        var exception = Assert.Throws<ConflictException>(order.Cancel);

        Assert.Equal(TransitionMessage(OrderStatus.Cancelled, OrderStatus.Cancelled), exception.Message);
    }

    [Fact]
    public void Cancel_from_Confirmed_throws_conflict()
    {
        var order = ConfirmedOrder();

        Assert.Throws<ConflictException>(order.Cancel);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Expire_from_Expired_throws_conflict()
    {
        var order = new Order();
        order.Expire();

        Assert.Throws<ConflictException>(order.Expire);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, false)]
    [InlineData(OrderStatus.AwaitingPayment, false)]
    [InlineData(OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Expired, true)]
    public void IsFinal_is_true_only_for_confirmed_cancelled_and_expired(OrderStatus status, bool expected)
    {
        var order = OrderInStatus(status);

        Assert.Equal(expected, order.IsFinal);
    }

    [Fact]
    public void EnsureCanBeChanged_AwaitingPayment_throws_conflict()
    {
        var order = new Order();
        order.StartPayment();

        var exception = Assert.Throws<ConflictException>(order.EnsureCanBeChanged);

        Assert.Equal(ResourceErrorMessages.ORDER_CANNOT_BE_CHANGED, exception.Message);
    }

    [Fact]
    public void SetItem_CancelledOrder_ThrowsConflict()
    {
        var order = new Order();
        order.Cancel();

        var exception = Assert.Throws<ConflictException>(() => order.SetItem(ProductBuilder.Build(), 1));

        Assert.Equal(ResourceErrorMessages.ORDER_CANNOT_BE_CHANGED, exception.Message);
        Assert.Empty(order.Items);
    }

    private static string TransitionMessage(OrderStatus from, OrderStatus to)
    {
        return string.Format(ResourceErrorMessages.ORDER_INVALID_STATUS_TRANSITION, from, to);
    }

    private static Order ConfirmedOrder()
    {
        var order = new Order();
        order.StartPayment();
        order.ConfirmPayment();
        return order;
    }

    private static Order OrderInStatus(OrderStatus status)
    {
        var order = new Order();

        switch (status)
        {
            case OrderStatus.AwaitingPayment:
                order.StartPayment();
                break;
            case OrderStatus.Confirmed:
                order.StartPayment();
                order.ConfirmPayment();
                break;
            case OrderStatus.Cancelled:
                order.Cancel();
                break;
            case OrderStatus.Expired:
                order.Expire();
                break;
        }

        return order;
    }
}
