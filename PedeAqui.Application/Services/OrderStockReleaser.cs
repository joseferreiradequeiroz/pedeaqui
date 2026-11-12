using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Domain.Repositories.Stock;

namespace PedeAqui.Application.Services;

public interface IOrderStockReleaser
{
    Task Release(Order order, string reason, CancellationToken cancellationToken);
}

public class OrderStockReleaser : IOrderStockReleaser
{
    private readonly IProductUpdateOnlyRepository _productRepository;
    private readonly IStockMovementRepository _movementRepository;

    public OrderStockReleaser(IProductUpdateOnlyRepository productRepository, IStockMovementRepository movementRepository)
    {
        _productRepository = productRepository;
        _movementRepository = movementRepository;
    }

    public async Task Release(Order order, string reason, CancellationToken cancellationToken)
    {
        var products = await _productRepository.GetByIdsForUpdate(order.Items.Select(item => item.ProductId), cancellationToken);
        var movements = new List<StockMovement>();

        foreach (var item in order.Items)
        {
            var product = products.First(product => product.Id == item.ProductId);
            movements.Add(product.AddStock(item.Quantity, reason, order));
            _productRepository.Update(product);
        }

        await _movementRepository.AddRange(movements, cancellationToken);
    }
}
