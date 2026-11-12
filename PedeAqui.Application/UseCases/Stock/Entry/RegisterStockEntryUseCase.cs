using AutoMapper;
using PedeAqui.Application.Validation;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Caching;
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
    private readonly IProductCacheInvalidator _cacheInvalidator;
    private readonly IMapper _mapper;

    public RegisterStockEntryUseCase(
        IProductUpdateOnlyRepository productRepository,
        IStockMovementRepository movementRepository,
        IUnitOfWork unitOfWork,
        IProductCacheInvalidator cacheInvalidator,
        IMapper mapper)
    {
        _productRepository = productRepository;
        _movementRepository = movementRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
        _mapper = mapper;
    }

    public async Task<StockMovementResponse> Execute(long productId, StockMovementRequest request, CancellationToken cancellationToken)
    {
        new StockMovementValidator().Validate(request).ThrowIfInvalid();

        var movement = await _unitOfWork.ExecuteInTransaction(
            transactionCancellationToken => RegisterMovement(productId, request, transactionCancellationToken),
            cancellationToken);
        await _cacheInvalidator.Invalidate([productId], CancellationToken.None);

        return _mapper.Map<StockMovementResponse>(movement);
    }

    private async Task<StockMovement> RegisterMovement(long productId, StockMovementRequest request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdForUpdate(productId, cancellationToken)
            ?? throw new NotFoundException(ResourceErrorMessages.PRODUCT_NOT_FOUND);

        var movement = product.AddStock(request.Quantity, request.Reason);

        _productRepository.Update(product);
        await _movementRepository.Add(movement, cancellationToken);

        return movement;
    }
}
