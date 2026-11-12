using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories.Stock;

namespace PedeAqui.UnitTests.Fakes;

public sealed class InMemoryStockMovementRepository : IStockMovementRepository
{
    private readonly List<StockMovement> _movements = [];
    private long _nextId = 1;

    public IReadOnlyList<StockMovement> Movements => _movements;

    public Task Add(StockMovement movement)
    {
        movement.Id = _nextId++;
        _movements.Add(movement);
        return Task.CompletedTask;
    }

    public async Task AddRange(IEnumerable<StockMovement> movements)
    {
        foreach (var movement in movements)
            await Add(movement);
    }

    public Task<List<StockMovement>> GetByProduct(long productId)
    {
        return Task.FromResult(_movements
            .Where(movement => movement.ProductId == productId)
            .OrderByDescending(movement => movement.Id)
            .ToList());
    }
}
