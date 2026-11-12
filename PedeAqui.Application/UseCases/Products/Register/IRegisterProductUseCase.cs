using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;

namespace PedeAqui.Application.UseCases.Products.Register;

public interface IRegisterProductUseCase
{
    Task<ProductResponse> Execute(ProductRequest request);
}
