namespace PedeAqui.Application.UseCases.Orders.Cancel;

public interface ICancelOrderUseCase
{
    Task Execute(long id, CancellationToken cancellationToken);
}
