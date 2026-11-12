using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Products;

public interface IProductUpdateOnlyRepository
{
    Task<Product?> GetById(long id, CancellationToken cancellationToken);

    Task<Product?> GetByIdForUpdate(long id, CancellationToken cancellationToken);

    Task<List<Product>> GetByIdsForUpdate(IEnumerable<long> ids, CancellationToken cancellationToken);

    void Update(Product product);
}
