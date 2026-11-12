using Microsoft.EntityFrameworkCore;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories.Products;

namespace PedeAqui.Infrastructure.Database.Repositories;

internal class ProductRepository : IProductReadOnlyRepository, IProductWriteOnlyRepository, IProductUpdateOnlyRepository
{
    private readonly PedeAquiDbContext _dbContext;

    public ProductRepository(PedeAquiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Add(Product product, CancellationToken cancellationToken)
    {
        await _dbContext.Products.AddAsync(product, cancellationToken);
    }

    public async Task<List<Product>> GetAllActive(CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.Active)
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);
    }

    async Task<Product?> IProductReadOnlyRepository.GetById(long id, CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(product => product.Id == id && product.Active, cancellationToken);
    }

    async Task<Product?> IProductUpdateOnlyRepository.GetById(long id, CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .FirstOrDefaultAsync(product => product.Id == id && product.Active, cancellationToken);
    }

    public async Task<Product?> GetByIdForUpdate(long id, CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .FromSql($"""SELECT *, xmin FROM products WHERE "Id" = {id} AND "Active" FOR UPDATE""")
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<Product>> GetByIdsForUpdate(IEnumerable<long> ids, CancellationToken cancellationToken)
    {
        var distinctIds = ids.Distinct().ToArray();

        return await _dbContext.Products
            .FromSql($"""SELECT *, xmin FROM products WHERE "Id" = ANY({distinctIds}) ORDER BY "Id" FOR UPDATE""")
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsWithSku(string sku, long? ignoreProductId, CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .AnyAsync(product => product.Sku == sku && (ignoreProductId == null || product.Id != ignoreProductId), cancellationToken);
    }

    public void Update(Product product)
    {
        _dbContext.Products.Update(product);
    }
}
