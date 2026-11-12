using AutoMapper;
using PedeAqui.Application.Validation;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Domain.Repositories.Stock;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Stock.Entry;

public class RegisterStockEntryUseCase : IRegisterStockEntryUseCase
{
    private readonly IProductUpdateOnlyRepository _productRepository;
    private readonly IStockMovementRepository _movementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public RegisterStockEntryUseCase(
        IProductUpdateOnlyRepository productRepository,
        IStockMovementRepository movementRepository,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _productRepository = productRepository;
        _movementRepository = movementRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<StockMovementResponse> Execute(long productId, StockMovementRequest request)
    {
        new StockMovementValidator().Validate(request).ThrowIfInvalid();

        var product = await _productRepository.GetById(productId)
            ?? throw new NotFoundException(ResourceErrorMessages.PRODUCT_NOT_FOUND);

        var movement = product.AddStock(request.Quantity, request.Reason);

        _productRepository.Update(product);
        await _movementRepository.Add(movement);
        await _unitOfWork.Commit();

        return _mapper.Map<StockMovementResponse>(movement);
    }
}
