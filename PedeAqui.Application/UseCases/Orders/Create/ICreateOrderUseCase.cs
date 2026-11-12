using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;

namespace PedeAqui.Application.UseCases.Orders.Create;

public interface ICreateOrderUseCase
{
    Task<OrderResponse> Execute(OrderRequest request, CancellationToken cancellationToken);
}
