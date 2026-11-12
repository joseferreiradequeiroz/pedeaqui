using PedeAqui.Communication.Responses;

namespace PedeAqui.Application.UseCases.Products.GetAll;

public interface IGetAllProductsUseCase
{
    Task<ProductsResponse> Execute(CancellationToken cancellationToken);
}
