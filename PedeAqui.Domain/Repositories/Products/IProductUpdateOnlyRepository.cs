using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Products;

public interface IProductUpdateOnlyRepository
{
    Task<Product?> GetById(long id);

    Task<List<Product>> GetByIds(IEnumerable<long> ids);

    void Update(Product product);
}
