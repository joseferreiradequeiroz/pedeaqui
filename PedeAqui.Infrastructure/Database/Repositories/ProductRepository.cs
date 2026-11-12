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

    public async Task Add(Product product)
    {
        await _dbContext.Products.AddAsync(product);
    }

    public async Task<List<Product>> GetAllActive()
    {
        return await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.Active)
            .OrderBy(product => product.Name)
            .ToListAsync();
    }

    async Task<Product?> IProductReadOnlyRepository.GetById(long id)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(product => product.Id == id && product.Active);
    }

    async Task<Product?> IProductUpdateOnlyRepository.GetById(long id)
    {
        return await _dbContext.Products
            .FirstOrDefaultAsync(product => product.Id == id && product.Active);
    }

    public async Task<List<Product>> GetByIds(IEnumerable<long> ids)
    {
        var distinctIds = ids.Distinct().ToList();

        return await _dbContext.Products
            .Where(product => distinctIds.Contains(product.Id))
            .ToListAsync();
    }

    public async Task<bool> ExistsWithSku(string sku, long? ignoreProductId = null)
    {
        return await _dbContext.Products
            .AnyAsync(product => product.Sku == sku && (ignoreProductId == null || product.Id != ignoreProductId));
    }

    public void Update(Product product)
    {
        _dbContext.Products.Update(product);
    }
}
