using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories.Products;

namespace PedeAqui.UnitTests.Fakes;

public sealed class InMemoryProductRepository : IProductReadOnlyRepository, IProductWriteOnlyRepository, IProductUpdateOnlyRepository
{
    private readonly List<Product> _products = [];
    private long _nextId = 1;

    public InMemoryProductRepository(params Product[] products)
    {
        foreach (var product in products)
            Store(product);
    }

    public IReadOnlyList<Product> Products => _products;

    public Task Add(Product product)
    {
        Store(product);
        return Task.CompletedTask;
    }

    public Task<List<Product>> GetAllActive() => Task.FromResult(_products.Where(product => product.Active).ToList());

    public Task<Product?> GetById(long id) => Task.FromResult(_products.FirstOrDefault(product => product.Id == id && product.Active));

    public Task<List<Product>> GetByIds(IEnumerable<long> ids)
    {
        var wanted = ids.ToHashSet();
        return Task.FromResult(_products.Where(product => wanted.Contains(product.Id)).ToList());
    }

    public Task<bool> ExistsWithSku(string sku, long? ignoreProductId = null)
    {
        return Task.FromResult(_products.Any(product => product.Sku == sku && product.Id != ignoreProductId));
    }

    public void Update(Product product)
    {
    }

    private void Store(Product product)
    {
        if (product.Id == 0)
            product.Id = _nextId;

        _nextId = Math.Max(_nextId, product.Id) + 1;
        _products.Add(product);
    }
}
