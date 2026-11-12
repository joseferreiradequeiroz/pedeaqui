using PedeAqui.Application.UseCases.Orders.Expire;
using PedeAqui.Domain.Enums;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests.Jobs;

[Collection(IntegrationCollection.Name)]
public class PendingOrderExpirerTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private static readonly TimeSpan BeyondExpirationWindow = TimeSpan.FromMinutes(16);
    private static readonly TimeSpan InsideExpirationWindow = TimeSpan.FromMinutes(14);

    private readonly PedeAquiWebApplicationFactory _factory;

    public PendingOrderExpirerTests(PedeAquiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Expired_pending_order_returns_stock()
    {
        await using var jobs = await JobsHarness.Create(_factory);
        var (order, productId) = await jobs.CreateOrder(price: 40m, quantity: 3);
        Assert.Equal(7, await jobs.StockOf(productId));
        jobs.Clock.Advance(BeyondExpirationWindow);

        var expired = await jobs.Run<IExpirePendingOrdersUseCase>();

        Assert.True(expired >= 1);
        Assert.Equal(OrderStatus.Expired, (await jobs.OrderOf(order.Id)).Status);
        Assert.Equal(10, await jobs.StockOf(productId));
    }

    [Fact]
    public async Task Fresh_pending_order_is_not_expired()
    {
        await using var jobs = await JobsHarness.Create(_factory);
        var (order, productId) = await jobs.CreateOrder(price: 40m, quantity: 3);
        jobs.Clock.Advance(InsideExpirationWindow);

        await jobs.Run<IExpirePendingOrdersUseCase>();

        Assert.Equal(OrderStatus.Pending, (await jobs.OrderOf(order.Id)).Status);
        Assert.Equal(7, await jobs.StockOf(productId));
    }

    [Fact]
    public async Task Awaiting_payment_order_is_not_expired()
    {
        await using var jobs = await JobsHarness.Create(_factory);
        var (order, productId) = await jobs.CreateOrder(price: 40m, quantity: 3);
        await jobs.MovePaymentToAwaiting(order.Id);
        jobs.Clock.Advance(BeyondExpirationWindow);

        await jobs.Run<IExpirePendingOrdersUseCase>();

        Assert.Equal(OrderStatus.AwaitingPayment, (await jobs.OrderOf(order.Id)).Status);
        Assert.Equal(7, await jobs.StockOf(productId));
    }
}
