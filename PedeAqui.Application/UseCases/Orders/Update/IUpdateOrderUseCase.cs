using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;

namespace PedeAqui.Application.UseCases.Orders.Update;

public interface IUpdateOrderUseCase
{
    Task<OrderResponse> Execute(long id, OrderRequest request);
}
