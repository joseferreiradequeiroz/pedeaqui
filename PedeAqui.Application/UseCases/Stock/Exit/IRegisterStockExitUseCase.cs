using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;

namespace PedeAqui.Application.UseCases.Stock.Exit;

public interface IRegisterStockExitUseCase
{
    Task<StockMovementResponse> Execute(long productId, StockMovementRequest request);
}
