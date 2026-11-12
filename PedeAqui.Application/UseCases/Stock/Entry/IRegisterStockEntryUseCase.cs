using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;

namespace PedeAqui.Application.UseCases.Stock.Entry;

public interface IRegisterStockEntryUseCase
{
    Task<StockMovementResponse> Execute(long productId, StockMovementRequest request);
}
