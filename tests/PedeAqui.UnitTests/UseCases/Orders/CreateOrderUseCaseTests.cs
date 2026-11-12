using PedeAqui.Application.UseCases.Orders.Create;
using PedeAqui.Communication.Enums;
using PedeAqui.Domain.Entities;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Fakes;
using PedeAqui.UnitTests.Support;

namespace PedeAqui.UnitTests.UseCases.Orders;

public class CreateOrderUseCaseTests
{
    private readonly User _customer = UserBuilder.Build(id: 1);
    private readonly InMemoryOrderRepository _orders = new();
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Execute_ValidRequest_CreatesPendingOrderForLoggedUser()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10, price: 50m);
        var soda = ProductBuilder.Build(id: 2, stock: 10, price: 8m);

        var response = await CreateUseCase(pizza, soda).Execute(RequestBuilder.Order((1, 2), (2, 3)));

        var order = Assert.Single(_orders.Orders);
        Assert.Equal(_customer.Id, order.UserId);
        Assert.Equal(order.Id, response.Id);
        Assert.Equal(OrderStatus.Pending, response.Status);
        Assert.Equal(124m, response.Total);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal(1, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Execute_ValidRequest_DecreasesStockAndRecordsExitPerItem()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var soda = ProductBuilder.Build(id: 2, stock: 5);

        await CreateUseCase(pizza, soda).Execute(RequestBuilder.Order((1, 2), (2, 5)));

        Assert.Equal(8, pizza.StockQuantity);
        Assert.Equal(0, soda.StockQuantity);
        Assert.Equal(2, _movements.Movements.Count);
        Assert.All(_movements.Movements, movement => Assert.Same(_orders.Orders[0], movement.Order));
    }

    [Fact]
    public async Task Execute_InsufficientStock_ThrowsAndDoesNotCommit()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var soda = ProductBuilder.Build(id: 2, stock: 1);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => CreateUseCase(pizza, soda).Execute(RequestBuilder.Order((1, 2), (2, 3))));

        Assert.StartsWith(ResourceErrorMessages.INSUFFICIENT_STOCK, exception.Message);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Execute_UnknownProduct_ThrowsNotFound()
    {
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => CreateUseCase().Execute(RequestBuilder.Order((42, 1))));

        Assert.StartsWith(ResourceErrorMessages.PRODUCT_NOT_FOUND, exception.Message);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Execute_InactiveProduct_ThrowsAndKeepsStock()
    {
        var product = ProductBuilder.Inactive(id: 1, stock: 10);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(
            () => CreateUseCase(product).Execute(RequestBuilder.Order((1, 1))));

        Assert.StartsWith(ResourceErrorMessages.PRODUCT_INACTIVE, exception.Message);
        Assert.Equal(10, product.StockQuantity);
    }

    [Fact]
    public async Task Execute_InvalidRequest_ThrowsValidationBeforeTouchingStock()
    {
        var product = ProductBuilder.Build(id: 1, stock: 10);

        await Assert.ThrowsAsync<ErrorOnValidationException>(
            () => CreateUseCase(product).Execute(RequestBuilder.Order()));

        Assert.Empty(_orders.Orders);
        Assert.Equal(10, product.StockQuantity);
    }

    private CreateOrderUseCase CreateUseCase(params Product[] products)
    {
        return new CreateOrderUseCase(
            _orders, new InMemoryProductRepository(products), _movements, _unitOfWork, new FakeLoggedUser(_customer), MapperFactory.Create());
    }
}
