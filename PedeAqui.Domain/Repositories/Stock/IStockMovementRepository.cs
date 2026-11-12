using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Stock;

public interface IStockMovementRepository
{
    Task Add(StockMovement movement);
    Task AddRange(IEnumerable<StockMovement> movements);
    Task<List<StockMovement>> GetByProduct(long productId);
}
