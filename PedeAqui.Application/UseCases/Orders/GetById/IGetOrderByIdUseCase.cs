using PedeAqui.Communication.Responses;

namespace PedeAqui.Application.UseCases.Orders.GetById;

public interface IGetOrderByIdUseCase
{
    Task<OrderResponse> Execute(long id);
}
