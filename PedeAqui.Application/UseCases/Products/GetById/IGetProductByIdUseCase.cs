using PedeAqui.Communication.Responses;

namespace PedeAqui.Application.UseCases.Products.GetById;

public interface IGetProductByIdUseCase
{
    Task<ProductResponse> Execute(long id);
}
