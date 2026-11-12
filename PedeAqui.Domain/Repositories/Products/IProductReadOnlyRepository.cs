using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Products;

public interface IProductReadOnlyRepository
{
    Task<List<Product>> GetAllActive();

    Task<Product?> GetById(long id);

    Task<bool> ExistsWithSku(string sku, long? ignoreProductId = null);
}
