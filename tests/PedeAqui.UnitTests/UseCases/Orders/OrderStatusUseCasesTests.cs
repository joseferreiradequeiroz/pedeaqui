using PedeAqui.Application.Services;
using PedeAqui.Application.UseCases.Orders.Cancel;
using PedeAqui.Application.UseCases.Orders.GetAll;
using PedeAqui.Application.UseCases.Orders.GetById;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Fakes;
using PedeAqui.UnitTests.Support;

namespace PedeAqui.UnitTests.UseCases.Orders;

public class OrderStatusUseCasesTests
{
    private readonly User _customer = UserBuilder.Build(id: 1);
    private readonly User _admin = UserBuilder.Admin();
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeProductCacheInvalidator _cache;

    public OrderStatusUseCasesTests()
    {
        _cache = new FakeProductCacheInvalidator(_unitOfWork);
    }

    [Fact]
    public async Task Cancel_PendingOrder_CancelsAndReturnsStock()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var order = OrderFactory.PendingWith(_customer, (pizza, 4));

        await CreateCancelUseCase(_customer, order, pizza).Execute(order.Id, CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(10, pizza.StockQuantity);
        Assert.Equal(StockMovementType.Entry, Assert.Single(_movements.Movements).Type);
        Assert.Equal(1, _unitOfWork.CommitCount);
        Assert.Equal([pizza.Id], _cache.InvalidatedProductIds);
        Assert.Equal(1, _cache.CommitCountAtLastInvalidation);
    }

    [Fact]
    public async Task Cancel_ByAdmin_CancelsOrderOfAnyCustomer()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var order = OrderFactory.PendingWith(_customer, (pizza, 1));

        await CreateCancelUseCase(_admin, order, pizza).Execute(order.Id, CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task Cancel_ConfirmedOrder_ThrowsConflictAndKeepsStock()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var order = OrderFactory.PendingWith(_customer, (pizza, 4));
        order.StartPayment();
        order.ConfirmPayment();

        await Assert.ThrowsAsync<ConflictException>(() => CreateCancelUseCase(_customer, order, pizza).Execute(order.Id, CancellationToken.None));

        Assert.Equal(6, pizza.StockQuantity);
        Assert.Equal(0, _unitOfWork.CommitCount);
        Assert.Equal(0, _cache.InvalidationCount);
    }

    [Fact]
    public async Task Cancel_OrderOfAnotherCustomer_ThrowsNotFound()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var order = OrderFactory.PendingWith(UserBuilder.Build(id: 2), (pizza, 1));

        await Assert.ThrowsAsync<NotFoundException>(() => CreateCancelUseCase(_customer, order, pizza).Execute(order.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_AwaitingPaymentOrder_ThrowsConflictAndKeepsStock()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var order = OrderFactory.PendingWith(_customer, (pizza, 4));
        order.StartPayment();

        await Assert.ThrowsAsync<ConflictException>(() => CreateCancelUseCase(_customer, order, pizza).Execute(order.Id, CancellationToken.None));

        Assert.Equal(6, pizza.StockQuantity);
        Assert.Empty(_movements.Movements);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task GetAll_Customer_SeesOnlyOwnOrders()
    {
        var own = OrderFactory.PendingWith(_customer, (ProductBuilder.Build(), 1));
        var other = OrderFactory.PendingWith(UserBuilder.Build(id: 2), (ProductBuilder.Build(), 1));
        var repository = new InMemoryOrderRepository(own, other);

        var response = await new GetAllOrdersUseCase(repository, new FakeLoggedUser(_customer), MapperFactory.Create()).Execute(CancellationToken.None);

        Assert.Equal(own.Id, Assert.Single(response.Orders).Id);
    }

    [Fact]
    public async Task GetAll_Admin_SeesAllOrders()
    {
        var first = OrderFactory.PendingWith(_customer, (ProductBuilder.Build(), 1));
        var second = OrderFactory.PendingWith(UserBuilder.Build(id: 2), (ProductBuilder.Build(), 1));
        var repository = new InMemoryOrderRepository(first, second);

        var response = await new GetAllOrdersUseCase(repository, new FakeLoggedUser(_admin), MapperFactory.Create()).Execute(CancellationToken.None);

        Assert.Equal(2, response.Orders.Count);
    }

    [Fact]
    public async Task GetById_OwnOrder_ReturnsItemsAndTotal()
    {
        var pizza = ProductBuilder.Build(price: 30m);
        var order = OrderFactory.PendingWith(_customer, (pizza, 2));

        var response = await new GetOrderByIdUseCase(new InMemoryOrderRepository(order), new FakeLoggedUser(_customer), MapperFactory.Create())
            .Execute(order.Id, CancellationToken.None);

        Assert.Equal(60m, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal(pizza.Name, item.ProductName);
        Assert.Equal(60m, item.Subtotal);
    }

    private CancelOrderUseCase CreateCancelUseCase(User loggedUser, Order order, params Product[] products)
    {
        return new CancelOrderUseCase(
            new InMemoryOrderRepository(order),
            new OrderStockReleaser(new InMemoryProductRepository(products), _movements),
            _unitOfWork,
            _cache,
            new FakeLoggedUser(loggedUser));
    }
}
