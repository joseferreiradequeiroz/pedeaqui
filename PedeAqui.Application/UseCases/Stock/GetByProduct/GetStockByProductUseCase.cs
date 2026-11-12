using AutoMapper;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Domain.Repositories.Stock;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Stock.GetByProduct;

public class GetStockByProductUseCase : IGetStockByProductUseCase
{
    private readonly IProductReadOnlyRepository _productRepository;
    private readonly IStockMovementRepository _movementRepository;
    private readonly IMapper _mapper;

    public GetStockByProductUseCase(
        IProductReadOnlyRepository productRepository,
        IStockMovementRepository movementRepository,
        IMapper mapper)
    {
        _productRepository = productRepository;
        _movementRepository = movementRepository;
        _mapper = mapper;
    }

    public async Task<StockResponse> Execute(long productId)
    {
        var product = await _productRepository.GetById(productId)
            ?? throw new NotFoundException(ResourceErrorMessages.PRODUCT_NOT_FOUND);

        var movements = await _movementRepository.GetByProduct(productId);

        return new StockResponse
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Quantity = product.StockQuantity,
            Movements = _mapper.Map<List<StockMovementResponse>>(movements)
        };
    }
}
