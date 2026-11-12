using PedeAqui.Application.UseCases.Orders.Update;
using PedeAqui.Domain.Entities;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Fakes;
using PedeAqui.UnitTests.Support;

namespace PedeAqui.UnitTests.UseCases.Orders;

public class UpdateOrderUseCaseTests
{
    private readonly User _customer = UserBuilder.Build(id: 1);
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeProductCacheInvalidator _cache;

    public UpdateOrderUseCaseTests()
    {
        _cache = new FakeProductCacheInvalidator(_unitOfWork);
    }

    [Fact]
    public async Task Execute_IncreaseQuantity_RemovesOnlyTheDifferenceFromStock()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var order = OrderFactory.PendingWith(_customer, (pizza, 2));

        var response = await CreateUseCase(order, pizza).Execute(order.Id, RequestBuilder.Order((1, 5)), CancellationToken.None);

        Assert.Equal(5, pizza.StockQuantity);
        Assert.Equal(5, Assert.Single(response.Items).Quantity);
        Assert.Equal(3, Assert.Single(_movements.Movements).Quantity);
        Assert.Equal(1, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Execute_DecreaseQuantity_ReturnsDifferenceToStock()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var order = OrderFactory.PendingWith(_customer, (pizza, 4));

        await CreateUseCase(order, pizza).Execute(order.Id, RequestBuilder.Order((1, 1)), CancellationToken.None);

        Assert.Equal(9, pizza.StockQuantity);
    }

    [Fact]
    public async Task Execute_ReplaceProduct_ReturnsOldStockAndTakesNewStock()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var soda = ProductBuilder.Build(id: 2, stock: 10);
        var order = OrderFactory.PendingWith(_customer, (pizza, 3));

        var response = await CreateUseCase(order, pizza, soda).Execute(order.Id, RequestBuilder.Order((2, 2)), CancellationToken.None);

        Assert.Equal(10, pizza.StockQuantity);
        Assert.Equal(8, soda.StockQuantity);
        Assert.Equal(soda.Id, Assert.Single(response.Items).ProductId);
        Assert.Equal(2, _movements.Movements.Count);
    }

    [Fact]
    public async Task Execute_ReplaceProduct_InvalidatesBothProductsAfterCommit()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var soda = ProductBuilder.Build(id: 2, stock: 10);
        var order = OrderFactory.PendingWith(_customer, (pizza, 3));

        await CreateUseCase(order, pizza, soda).Execute(order.Id, RequestBuilder.Order((2, 2)), CancellationToken.None);

        Assert.Equal([1L, 2L], _cache.InvalidatedProductIds.Order());
        Assert.Equal(1, _cache.CommitCountAtLastInvalidation);
    }

    [Fact]
    public async Task Execute_IncreaseBeyondStock_ThrowsAndDoesNotCommit()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 3);
        var order = OrderFactory.PendingWith(_customer, (pizza, 2));

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => CreateUseCase(order, pizza).Execute(order.Id, RequestBuilder.Order((1, 5)), CancellationToken.None));

        Assert.Equal(0, _unitOfWork.CommitCount);
        Assert.Equal(0, _cache.InvalidationCount);
    }

    [Fact]
    public async Task Execute_ConfirmedOrder_ThrowsConflictCannotBeChanged()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var order = OrderFactory.PendingWith(_customer, (pizza, 1));
        order.StartPayment();
        order.ConfirmPayment();

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => CreateUseCase(order, pizza).Execute(order.Id, RequestBuilder.Order((1, 2)), CancellationToken.None));

        Assert.Equal(ResourceErrorMessages.ORDER_CANNOT_BE_CHANGED, exception.Message);
    }

    [Fact]
    public async Task Execute_AwaitingPaymentOrder_ThrowsConflictAndKeepsStock()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var order = OrderFactory.PendingWith(_customer, (pizza, 1));
        order.StartPayment();

        await Assert.ThrowsAsync<ConflictException>(
            () => CreateUseCase(order, pizza).Execute(order.Id, RequestBuilder.Order((1, 4)), CancellationToken.None));

        Assert.Equal(9, pizza.StockQuantity);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Execute_OrderOfAnotherCustomer_ThrowsNotFound()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var order = OrderFactory.PendingWith(UserBuilder.Build(id: 2), (pizza, 1));

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => CreateUseCase(order, pizza).Execute(order.Id, RequestBuilder.Order((1, 2)), CancellationToken.None));

        Assert.Equal(ResourceErrorMessages.ORDER_NOT_FOUND, exception.Message);
    }

    private UpdateOrderUseCase CreateUseCase(Order order, params Product[] products)
    {
        return new UpdateOrderUseCase(
            new InMemoryOrderRepository(order),
            new InMemoryProductRepository(products),
            _movements,
            _unitOfWork,
            _cache,
            new FakeLoggedUser(_customer),
            MapperFactory.Create(),
            RequestLimitsFactory.CreateOptions());
    }
}
