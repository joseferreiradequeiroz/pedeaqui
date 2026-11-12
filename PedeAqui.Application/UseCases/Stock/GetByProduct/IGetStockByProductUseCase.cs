using PedeAqui.Communication.Responses;

namespace PedeAqui.Application.UseCases.Stock.GetByProduct;

public interface IGetStockByProductUseCase
{
    Task<StockResponse> Execute(long productId);
}
