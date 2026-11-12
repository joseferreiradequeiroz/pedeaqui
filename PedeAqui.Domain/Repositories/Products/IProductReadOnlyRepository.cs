using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Products;

public interface IProductReadOnlyRepository
{
    Task<List<Product>> GetAllActive(CancellationToken cancellationToken);

    Task<Product?> GetById(long id, CancellationToken cancellationToken);

    Task<bool> ExistsWithSku(string sku, long? ignoreProductId, CancellationToken cancellationToken);
}
