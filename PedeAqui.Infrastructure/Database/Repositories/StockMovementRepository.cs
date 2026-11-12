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

    public async Task Add(StockMovement movement)
    {
        await _dbContext.StockMovements.AddAsync(movement);
    }

    public async Task AddRange(IEnumerable<StockMovement> movements)
    {
        await _dbContext.StockMovements.AddRangeAsync(movements);
    }

    public async Task<List<StockMovement>> GetByProduct(long productId)
    {
        return await _dbContext.StockMovements
            .AsNoTracking()
            .Where(movement => movement.ProductId == productId)
            .OrderByDescending(movement => movement.CreatedAt)
            .ThenByDescending(movement => movement.Id)
            .ToListAsync();
    }
}
