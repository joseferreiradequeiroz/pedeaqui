using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PedeAqui.Application.Services;
using PedeAqui.Application.Settings;
using PedeAqui.Application.UseCases.Orders.Expire;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Fakes;
using PedeAqui.UnitTests.Support;

namespace PedeAqui.UnitTests.UseCases.Jobs;

public class ExpirePendingOrdersUseCaseTests
{
    private const int EXPIRATION_MINUTES = 15;

    private readonly User _customer = UserBuilder.Build(id: 1);
    private readonly Product _pizza = ProductBuilder.Build(id: 1, stock: 10, price: 40m);
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeProductCacheInvalidator _cache;
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    public ExpirePendingOrdersUseCaseTests()
    {
        _cache = new FakeProductCacheInvalidator(_unitOfWork);
    }

    [Fact]
    public async Task Pending_order_older_than_the_window_expires_and_returns_stock()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 3));
        _clock.Advance(TimeSpan.FromMinutes(EXPIRATION_MINUTES + 1));

        var expired = await UseCaseFor(batchSize: 100, order).Execute(CancellationToken.None);

        Assert.Equal(1, expired);
        Assert.Equal(OrderStatus.Expired, order.Status);
        Assert.Equal(10, _pizza.StockQuantity);
        var returned = Assert.Single(_movements.Movements);
        Assert.Equal(StockMovementType.Entry, returned.Type);
        Assert.Equal(3, returned.Quantity);
        Assert.Equal([_pizza.Id], _cache.InvalidatedProductIds);
        Assert.Equal(_unitOfWork.CommitCount, _cache.CommitCountAtLastInvalidation);
    }

    [Fact]
    public async Task Pending_order_inside_the_window_is_kept()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 3));
        _clock.Advance(TimeSpan.FromMinutes(EXPIRATION_MINUTES - 1));

        var expired = await UseCaseFor(batchSize: 100, order).Execute(CancellationToken.None);

        Assert.Equal(0, expired);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(7, _pizza.StockQuantity);
        Assert.Empty(_movements.Movements);
        Assert.Equal(0, _cache.InvalidationCount);
    }

    [Fact]
    public async Task Order_awaiting_payment_is_never_expired()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 3));
        order.StartPayment();
        _clock.Advance(TimeSpan.FromMinutes(EXPIRATION_MINUTES * 10));

        var expired = await UseCaseFor(batchSize: 100, order).Execute(CancellationToken.None);

        Assert.Equal(0, expired);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(7, _pizza.StockQuantity);
    }

    [Fact]
    public async Task Order_that_started_payment_after_being_selected_is_skipped_and_the_others_still_expire()
    {
        var racedOrder = OrderFactory.PendingWith(_customer, (_pizza, 2));
        var plainOrder = OrderFactory.PendingWith(_customer, (_pizza, 1));
        var orders = new InMemoryOrderRepository(racedOrder, plainOrder)
        {
            WhenLoadedForSystem = order =>
            {
                if (order == racedOrder && order.Status == OrderStatus.Pending)
                    order.StartPayment();
            }
        };
        _clock.Advance(TimeSpan.FromMinutes(EXPIRATION_MINUTES + 1));

        var expired = await UseCaseFor(batchSize: 100, orders).Execute(CancellationToken.None);

        Assert.Equal(1, expired);
        Assert.Equal(OrderStatus.AwaitingPayment, racedOrder.Status);
        Assert.Equal(OrderStatus.Expired, plainOrder.Status);
        Assert.Equal(8, _pizza.StockQuantity);
    }

    [Fact]
    public async Task Keeps_expiring_batch_after_batch_until_none_are_left()
    {
        var orders = Enumerable.Range(0, 5).Select(_ => OrderFactory.PendingWith(_customer, (_pizza, 1))).ToArray();
        _clock.Advance(TimeSpan.FromMinutes(EXPIRATION_MINUTES + 1));

        var expired = await UseCaseFor(batchSize: 2, orders).Execute(CancellationToken.None);

        Assert.Equal(5, expired);
        Assert.All(orders, order => Assert.Equal(OrderStatus.Expired, order.Status));
        Assert.Equal(10, _pizza.StockQuantity);
    }

    private ExpirePendingOrdersUseCase UseCaseFor(int batchSize, params Order[] orders) =>
        UseCaseFor(batchSize, new InMemoryOrderRepository(orders));

    private ExpirePendingOrdersUseCase UseCaseFor(int batchSize, InMemoryOrderRepository orders)
    {
        var products = new InMemoryProductRepository(_pizza);

        return new ExpirePendingOrdersUseCase(
            orders,
            new OrderStockReleaser(products, _movements),
            _unitOfWork,
            _cache,
            _clock,
            Options.Create(new OrdersOptions { PendingExpirationMinutes = EXPIRATION_MINUTES, ExpirationBatchSize = batchSize }));
    }
}
