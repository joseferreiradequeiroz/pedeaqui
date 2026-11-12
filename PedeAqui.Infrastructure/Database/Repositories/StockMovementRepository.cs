using Microsoft.EntityFrameworkCore;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories.Stock;

namespace PedeAqui.Infrastructure.Database.Repositories;

internal class StockMovementRepository : IStockMovementRepository
{
    private readonly PedeAquiDbContext _dbContext;

    public StockMovementRepository(PedeAquiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Add(StockMovement movement, CancellationToken cancellationToken)
    {
        await _dbContext.StockMovements.AddAsync(movement, cancellationToken);
    }

    public async Task AddRange(IEnumerable<StockMovement> movements, CancellationToken cancellationToken)
    {
        await _dbContext.StockMovements.AddRangeAsync(movements, cancellationToken);
    }

    public async Task<List<StockMovement>> GetByProduct(long productId, CancellationToken cancellationToken)
    {
        return await _dbContext.StockMovements
            .AsNoTracking()
            .Where(movement => movement.ProductId == productId)
            .OrderByDescending(movement => movement.CreatedAt)
            .ThenByDescending(movement => movement.Id)
            .ToListAsync(cancellationToken);
    }
}
