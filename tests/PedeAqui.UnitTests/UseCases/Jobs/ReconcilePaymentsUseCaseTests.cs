using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PedeAqui.Application.Services;
using PedeAqui.Application.Settings;
using PedeAqui.Application.UseCases.Payments.Reconcile;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Domain.Payments;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Fakes;
using PedeAqui.UnitTests.Support;

namespace PedeAqui.UnitTests.UseCases.Jobs;

public class ReconcilePaymentsUseCaseTests
{
    private const int RECONCILE_AFTER_MINUTES = 10;

    private readonly User _customer = UserBuilder.Build(id: 1);
    private readonly Product _pizza = ProductBuilder.Build(id: 1, stock: 10, price: 40m);
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly FakePaymentGateway _gateway = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeProductCacheInvalidator _cache;
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private long _nextOrderId = 1;

    public ReconcilePaymentsUseCaseTests()
    {
        _cache = new FakeProductCacheInvalidator(_unitOfWork);
    }

    [Fact]
    public async Task Order_approved_at_the_gateway_is_confirmed()
    {
        var order = AwaitingPaymentOrder(quantity: 2);
        _gateway.PaymentsByKey[KeyOf(order)] = new PaymentResult(PaymentStatus.Approved, "payment-1");
        _clock.Advance(TimeSpan.FromMinutes(RECONCILE_AFTER_MINUTES + 1));

        var reconciled = await UseCaseFor(batchSize: 100, order).Execute(CancellationToken.None);

        Assert.Equal(1, reconciled);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(8, _pizza.StockQuantity);
        Assert.Empty(_movements.Movements);
        Assert.Equal(0, _cache.InvalidationCount);
    }

    [Fact]
    public async Task Order_declined_at_the_gateway_is_cancelled_and_returns_stock()
    {
        var order = AwaitingPaymentOrder(quantity: 2);
        _gateway.PaymentsByKey[KeyOf(order)] = new PaymentResult(PaymentStatus.Declined, "payment-1");
        _clock.Advance(TimeSpan.FromMinutes(RECONCILE_AFTER_MINUTES + 1));

        var reconciled = await UseCaseFor(batchSize: 100, order).Execute(CancellationToken.None);

        Assert.Equal(1, reconciled);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(10, _pizza.StockQuantity);
        var returned = Assert.Single(_movements.Movements);
        Assert.Equal(StockMovementType.Entry, returned.Type);
        Assert.Equal([_pizza.Id], _cache.InvalidatedProductIds);
        Assert.Equal(_unitOfWork.CommitCount, _cache.CommitCountAtLastInvalidation);
    }

    [Fact]
    public async Task Order_unknown_to_the_gateway_is_cancelled_and_returns_stock()
    {
        var order = AwaitingPaymentOrder(quantity: 2);
        _clock.Advance(TimeSpan.FromMinutes(RECONCILE_AFTER_MINUTES + 1));

        var reconciled = await UseCaseFor(batchSize: 100, order).Execute(CancellationToken.None);

        Assert.Equal(1, reconciled);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(10, _pizza.StockQuantity);
        Assert.Equal([KeyOf(order)], _gateway.Lookups);
    }

    [Fact]
    public async Task Gateway_unavailable_stops_the_cycle_and_leaves_orders_untouched()
    {
        var first = AwaitingPaymentOrder(quantity: 1);
        var second = AwaitingPaymentOrder(quantity: 1);
        _gateway.FindFailure = new PaymentGatewayUnavailableException(null);
        _clock.Advance(TimeSpan.FromMinutes(RECONCILE_AFTER_MINUTES + 1));

        var reconciled = await UseCaseFor(batchSize: 100, first, second).Execute(CancellationToken.None);

        Assert.Equal(0, reconciled);
        Assert.Equal(OrderStatus.AwaitingPayment, first.Status);
        Assert.Equal(OrderStatus.AwaitingPayment, second.Status);
        Assert.Single(_gateway.Lookups);
        Assert.Equal(8, _pizza.StockQuantity);
        Assert.Empty(_movements.Movements);
    }

    [Fact]
    public async Task Gateway_rejecting_one_lookup_skips_that_order_and_reconciles_the_rest()
    {
        var rejected = AwaitingPaymentOrder(quantity: 1);
        var approved = AwaitingPaymentOrder(quantity: 1);
        _gateway.FindFailuresByKey[KeyOf(rejected)] = new PaymentGatewayRejectedException(System.Net.HttpStatusCode.BadRequest);
        _gateway.PaymentsByKey[KeyOf(approved)] = new PaymentResult(PaymentStatus.Approved, "payment-2");
        _clock.Advance(TimeSpan.FromMinutes(RECONCILE_AFTER_MINUTES + 1));

        var reconciled = await UseCaseFor(batchSize: 100, rejected, approved).Execute(CancellationToken.None);

        Assert.Equal(1, reconciled);
        Assert.Equal(OrderStatus.AwaitingPayment, rejected.Status);
        Assert.Equal(OrderStatus.Confirmed, approved.Status);
        Assert.Equal([KeyOf(rejected), KeyOf(approved)], _gateway.Lookups);
        Assert.Empty(_movements.Movements);
    }

    [Fact]
    public async Task Order_inside_the_window_is_not_looked_up()
    {
        var order = AwaitingPaymentOrder(quantity: 2);
        _clock.Advance(TimeSpan.FromMinutes(RECONCILE_AFTER_MINUTES - 1));

        var reconciled = await UseCaseFor(batchSize: 100, order).Execute(CancellationToken.None);

        Assert.Equal(0, reconciled);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Empty(_gateway.Lookups);
    }

    [Fact]
    public async Task Pending_order_is_never_reconciled()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 2));
        _clock.Advance(TimeSpan.FromMinutes(RECONCILE_AFTER_MINUTES * 10));

        var reconciled = await UseCaseFor(batchSize: 100, order).Execute(CancellationToken.None);

        Assert.Equal(0, reconciled);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Empty(_gateway.Lookups);
    }

    [Fact]
    public async Task Order_settled_by_the_consumer_while_being_looked_up_is_skipped()
    {
        var settledMeanwhile = AwaitingPaymentOrder(quantity: 2);
        var stillAwaiting = AwaitingPaymentOrder(quantity: 1);
        _gateway.PaymentsByKey[KeyOf(settledMeanwhile)] = new PaymentResult(PaymentStatus.Declined, "payment-1");
        _gateway.PaymentsByKey[KeyOf(stillAwaiting)] = new PaymentResult(PaymentStatus.Approved, "payment-2");
        _gateway.WhenFound = key =>
        {
            if (key == KeyOf(settledMeanwhile))
                settledMeanwhile.ConfirmPayment();
        };
        _clock.Advance(TimeSpan.FromMinutes(RECONCILE_AFTER_MINUTES + 1));

        var reconciled = await UseCaseFor(batchSize: 100, settledMeanwhile, stillAwaiting).Execute(CancellationToken.None);

        Assert.Equal(1, reconciled);
        Assert.Equal(OrderStatus.Confirmed, settledMeanwhile.Status);
        Assert.Equal(OrderStatus.Confirmed, stillAwaiting.Status);
        Assert.Equal(7, _pizza.StockQuantity);
        Assert.Empty(_movements.Movements);
    }

    [Fact]
    public async Task Keeps_reconciling_batch_after_batch_until_none_are_left()
    {
        var orders = Enumerable.Range(0, 5).Select(_ => AwaitingPaymentOrder(quantity: 1)).ToArray();
        foreach (var order in orders)
            _gateway.PaymentsByKey[KeyOf(order)] = new PaymentResult(PaymentStatus.Approved, $"payment-{order.Id}");
        _clock.Advance(TimeSpan.FromMinutes(RECONCILE_AFTER_MINUTES + 1));

        var reconciled = await UseCaseFor(batchSize: 2, orders).Execute(CancellationToken.None);

        Assert.Equal(5, reconciled);
        Assert.All(orders, order => Assert.Equal(OrderStatus.Confirmed, order.Status));
    }

    private Order AwaitingPaymentOrder(int quantity)
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, quantity));
        order.Id = _nextOrderId++;
        order.StartPayment();
        return order;
    }

    private static string KeyOf(Order order) => $"order-{order.Id}";

    private ReconcilePaymentsUseCase UseCaseFor(int batchSize, params Order[] orders)
    {
        var products = new InMemoryProductRepository(_pizza);

        return new ReconcilePaymentsUseCase(
            new InMemoryOrderRepository(orders),
            _gateway,
            new OrderStockReleaser(products, _movements),
            _unitOfWork,
            _cache,
            _clock,
            Options.Create(new PaymentsOptions { ReconcileAfterMinutes = RECONCILE_AFTER_MINUTES, ReconcileBatchSize = batchSize }),
            NullLogger<ReconcilePaymentsUseCase>.Instance);
    }
}
