namespace PedeAqui.Application.UseCases.Products.Delete;

public interface IDeleteProductUseCase
{
    Task Execute(long id, CancellationToken cancellationToken);
}
