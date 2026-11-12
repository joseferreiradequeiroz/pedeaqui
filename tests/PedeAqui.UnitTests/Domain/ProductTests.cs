using PedeAqui.Domain.Enums;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Support;

namespace PedeAqui.UnitTests.Domain;

public class ProductTests
{
    [Fact]
    public void AddStock_PositiveQuantity_IncreasesStockAndReturnsEntryMovement()
    {
        var product = ProductBuilder.Build(stock: 10);

        var movement = product.AddStock(5, "Compra");

        Assert.Equal(15, product.StockQuantity);
        Assert.Equal(StockMovementType.Entry, movement.Type);
        Assert.Equal(5, movement.Quantity);
        Assert.Equal(product.Id, movement.ProductId);
        Assert.Equal("Compra", movement.Reason);
    }

    [Fact]
    public void RemoveStock_QuantityAvailable_DecreasesStockAndReturnsExitMovement()
    {
        var product = ProductBuilder.Build(stock: 10);

        var movement = product.RemoveStock(4, "Venda");

        Assert.Equal(6, product.StockQuantity);
        Assert.Equal(StockMovementType.Exit, movement.Type);
        Assert.Equal(4, movement.Quantity);
    }

    [Fact]
    public void RemoveStock_ExactlyAllStock_LeavesZero()
    {
        var product = ProductBuilder.Build(stock: 3);

        product.RemoveStock(3, "Venda");

        Assert.Equal(0, product.StockQuantity);
    }

    [Fact]
    public void RemoveStock_MoreThanAvailable_ThrowsAndKeepsStock()
    {
        var product = ProductBuilder.Build(stock: 2);

        var exception = Assert.Throws<BusinessRuleException>(() => product.RemoveStock(3, "Venda"));

        Assert.StartsWith(ResourceErrorMessages.INSUFFICIENT_STOCK, exception.Message);
        Assert.Equal(2, product.StockQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddStock_ZeroOrNegative_Throws(int quantity)
    {
        var product = ProductBuilder.Build(stock: 0);

        var exception = Assert.Throws<BusinessRuleException>(() => product.AddStock(quantity, "Compra"));

        Assert.Equal(ResourceErrorMessages.STOCK_QUANTITY_MUST_BE_GREATER_THAN_ZERO, exception.Message);
        Assert.Equal(0, product.StockQuantity);
    }

    [Fact]
    public void RemoveStock_WithOrder_LinksMovementToOrder()
    {
        var product = ProductBuilder.Build(stock: 5);
        var order = OrderFactory.PendingWith(UserBuilder.Build());

        var movement = product.RemoveStock(1, "Venda", order);

        Assert.Same(order, movement.Order);
    }

    [Fact]
    public void EnsureCanBeSold_InactiveProduct_Throws()
    {
        var product = ProductBuilder.Inactive();

        var exception = Assert.Throws<BusinessRuleException>(product.EnsureCanBeSold);

        Assert.StartsWith(ResourceErrorMessages.PRODUCT_INACTIVE, exception.Message);
    }

    [Fact]
    public void Deactivate_ActiveProduct_MarksAsInactive()
    {
        var product = ProductBuilder.Build();

        product.Deactivate();

        Assert.False(product.Active);
    }
}
