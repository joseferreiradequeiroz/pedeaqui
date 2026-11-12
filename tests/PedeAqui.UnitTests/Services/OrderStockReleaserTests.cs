using PedeAqui.Application.Services;
using PedeAqui.Domain.Enums;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Fakes;
using PedeAqui.UnitTests.Support;

namespace PedeAqui.UnitTests.Services;

public class OrderStockReleaserTests
{
    private readonly InMemoryStockMovementRepository _movements = new();

    [Fact]
    public async Task Release_ReturnsEveryItemToStockAndRecordsMovementsWithReason()
    {
        var pizza = ProductBuilder.Build(id: 1, stock: 10);
        var soda = ProductBuilder.Build(id: 2, stock: 10);
        var order = OrderFactory.PendingWith(UserBuilder.Build(id: 1), (pizza, 4), (soda, 2));
        var releaser = new OrderStockReleaser(new InMemoryProductRepository(pizza, soda), _movements);

        await releaser.Release(order, "Expiração do pedido", CancellationToken.None);

        Assert.Equal(10, pizza.StockQuantity);
        Assert.Equal(10, soda.StockQuantity);
        Assert.Equal(2, _movements.Movements.Count);
        Assert.All(_movements.Movements, movement =>
        {
            Assert.Equal(StockMovementType.Entry, movement.Type);
            Assert.Equal("Expiração do pedido", movement.Reason);
        });
    }
}
