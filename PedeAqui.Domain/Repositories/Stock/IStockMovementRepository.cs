using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Stock;

public interface IStockMovementRepository
{
    Task Add(StockMovement movement, CancellationToken cancellationToken);
    Task AddRange(IEnumerable<StockMovement> movements, CancellationToken cancellationToken);
    Task<List<StockMovement>> GetByProduct(long productId, CancellationToken cancellationToken);
}
