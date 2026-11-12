using PedeAqui.Domain.Repositories;

namespace PedeAqui.Infrastructure.Database;

internal class UnitOfWork : IUnitOfWork
{
    private readonly PedeAquiDbContext _dbContext;

    public UnitOfWork(PedeAquiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Commit()
    {
        await _dbContext.SaveChangesAsync();
    }
}
