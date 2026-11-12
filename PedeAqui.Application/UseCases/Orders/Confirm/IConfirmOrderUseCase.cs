namespace PedeAqui.Application.UseCases.Orders.Confirm;

public interface IConfirmOrderUseCase
{
    Task Execute(long id);
}
