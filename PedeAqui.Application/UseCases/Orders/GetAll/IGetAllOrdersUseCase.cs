using PedeAqui.Communication.Responses;

namespace PedeAqui.Application.UseCases.Orders.GetAll;

public interface IGetAllOrdersUseCase
{
    Task<OrdersResponse> Execute();
}
