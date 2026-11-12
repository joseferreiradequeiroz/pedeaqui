using PedeAqui.Application.UseCases.Payments.Process;
using PedeAqui.Application.UseCases.Payments.Reconcile;
using PedeAqui.Domain.Enums;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests.Jobs;

[Collection(IntegrationCollection.Name)]
public class PaymentReconcilerTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private static readonly TimeSpan BeyondReconcileWindow = TimeSpan.FromMinutes(11);

    private readonly PedeAquiWebApplicationFactory _factory;

    public PaymentReconcilerTests(PedeAquiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Reconciler_confirms_order_approved_at_gateway()
    {
        await using var jobs = await JobsHarness.Create(_factory);
        var (order, productId) = await jobs.CreateOrder(price: 40m, quantity: 2);
        await jobs.MovePaymentToAwaiting(order.Id);
        await jobs.ChargeOnGateway(ProcessOrderPaymentUseCase.PaymentKeyOf(order.Id), amount: 80m);
        jobs.Clock.Advance(BeyondReconcileWindow);

        var reconciled = await jobs.Run<IReconcilePaymentsUseCase>();

        Assert.True(reconciled >= 1);
        Assert.Equal(OrderStatus.Confirmed, (await jobs.OrderOf(order.Id)).Status);
        Assert.Equal(8, await jobs.StockOf(productId));
    }

    [Fact]
    public async Task Reconciler_compensates_order_declined_at_gateway()
    {
        await using var jobs = await JobsHarness.Create(_factory);
        var (order, productId) = await jobs.CreateOrder(price: 25.13m, quantity: 1);
        await jobs.MovePaymentToAwaiting(order.Id);
        await jobs.ChargeOnGateway(ProcessOrderPaymentUseCase.PaymentKeyOf(order.Id), amount: 25.13m);
        jobs.Clock.Advance(BeyondReconcileWindow);

        var reconciled = await jobs.Run<IReconcilePaymentsUseCase>();

        Assert.True(reconciled >= 1);
        Assert.Equal(OrderStatus.Cancelled, (await jobs.OrderOf(order.Id)).Status);
        Assert.Equal(10, await jobs.StockOf(productId));
    }

    [Fact]
    public async Task Reconciler_compensates_order_unknown_to_gateway()
    {
        await using var jobs = await JobsHarness.Create(_factory);
        var (order, productId) = await jobs.CreateOrder(price: 40m, quantity: 2);
        await jobs.MovePaymentToAwaiting(order.Id);
        jobs.Clock.Advance(BeyondReconcileWindow);

        var reconciled = await jobs.Run<IReconcilePaymentsUseCase>();

        Assert.True(reconciled >= 1);
        Assert.Equal(OrderStatus.Cancelled, (await jobs.OrderOf(order.Id)).Status);
        Assert.Equal(10, await jobs.StockOf(productId));
    }

    [Fact]
    public async Task Reconciler_leaves_order_untouched_when_gateway_down()
    {
        await using var jobs = await JobsHarness.Create(_factory);
        var (order, productId) = await jobs.CreateOrder(price: 40m, quantity: 2);
        await jobs.MovePaymentToAwaiting(order.Id);
        await jobs.TakeGatewayDown();
        jobs.Clock.Advance(BeyondReconcileWindow);

        var reconciled = await jobs.Run<IReconcilePaymentsUseCase>();

        Assert.Equal(0, reconciled);
        Assert.Equal(OrderStatus.AwaitingPayment, (await jobs.OrderOf(order.Id)).Status);
        Assert.Equal(8, await jobs.StockOf(productId));
    }

    [Fact]
    public async Task Reconciler_ignores_order_still_inside_the_window()
    {
        await using var jobs = await JobsHarness.Create(_factory);
        var (order, productId) = await jobs.CreateOrder(price: 40m, quantity: 2);
        await jobs.MovePaymentToAwaiting(order.Id);
        jobs.Clock.Advance(TimeSpan.FromMinutes(9));

        await jobs.Run<IReconcilePaymentsUseCase>();

        Assert.Equal(OrderStatus.AwaitingPayment, (await jobs.OrderOf(order.Id)).Status);
        Assert.Equal(8, await jobs.StockOf(productId));
    }
}
