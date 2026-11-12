using PedeAqui.Communication.Requests;

namespace PedeAqui.Application.UseCases.Products.Update;

public interface IUpdateProductUseCase
{
    Task Execute(long id, ProductRequest request, CancellationToken cancellationToken);
}
