using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Products;

public interface IProductWriteOnlyRepository
{
    Task Add(Product product, CancellationToken cancellationToken);
}
