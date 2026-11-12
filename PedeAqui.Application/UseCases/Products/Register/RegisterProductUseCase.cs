using AutoMapper;
using FluentValidation.Results;
using PedeAqui.Application.Validation;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Caching;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Exception;

namespace PedeAqui.Application.UseCases.Products.Register;

public class RegisterProductUseCase : IRegisterProductUseCase
{
    private readonly IProductWriteOnlyRepository _writeOnlyRepository;
    private readonly IProductReadOnlyRepository _readOnlyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductCacheInvalidator _cacheInvalidator;
    private readonly IMapper _mapper;

    public RegisterProductUseCase(
        IProductWriteOnlyRepository writeOnlyRepository,
        IProductReadOnlyRepository readOnlyRepository,
        IUnitOfWork unitOfWork,
        IProductCacheInvalidator cacheInvalidator,
        IMapper mapper)
    {
        _writeOnlyRepository = writeOnlyRepository;
        _readOnlyRepository = readOnlyRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
        _mapper = mapper;
    }

    public async Task<ProductResponse> Execute(ProductRequest request, CancellationToken cancellationToken)
    {
        await Validate(request, cancellationToken);

        var product = _mapper.Map<Product>(request);

        await _writeOnlyRepository.Add(product, cancellationToken);
        await _unitOfWork.Commit(cancellationToken);
        await _cacheInvalidator.Invalidate([product.Id], CancellationToken.None);

        return _mapper.Map<ProductResponse>(product);
    }

    private async Task Validate(ProductRequest request, CancellationToken cancellationToken)
    {
        var result = new ProductValidator().Validate(request);

        if (!string.IsNullOrWhiteSpace(request.Sku) && await _readOnlyRepository.ExistsWithSku(request.Sku, ignoreProductId: null, cancellationToken))
            result.Errors.Add(new ValidationFailure(nameof(request.Sku), ResourceErrorMessages.PRODUCT_SKU_ALREADY_EXISTS));

        result.ThrowIfInvalid();
    }
}
