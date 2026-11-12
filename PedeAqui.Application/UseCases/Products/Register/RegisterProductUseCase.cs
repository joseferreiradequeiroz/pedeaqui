using AutoMapper;
using FluentValidation.Results;
using PedeAqui.Application.Validation;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Exception;

namespace PedeAqui.Application.UseCases.Products.Register;

public class RegisterProductUseCase : IRegisterProductUseCase
{
    private readonly IProductWriteOnlyRepository _writeOnlyRepository;
    private readonly IProductReadOnlyRepository _readOnlyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public RegisterProductUseCase(
        IProductWriteOnlyRepository writeOnlyRepository,
        IProductReadOnlyRepository readOnlyRepository,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _writeOnlyRepository = writeOnlyRepository;
        _readOnlyRepository = readOnlyRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<ProductResponse> Execute(ProductRequest request)
    {
        await Validate(request);

        var product = _mapper.Map<Product>(request);

        await _writeOnlyRepository.Add(product);
        await _unitOfWork.Commit();

        return _mapper.Map<ProductResponse>(product);
    }

    private async Task Validate(ProductRequest request)
    {
        var result = new ProductValidator().Validate(request);

        if (!string.IsNullOrWhiteSpace(request.Sku) && await _readOnlyRepository.ExistsWithSku(request.Sku))
            result.Errors.Add(new ValidationFailure(nameof(request.Sku), ResourceErrorMessages.PRODUCT_SKU_ALREADY_EXISTS));

        result.ThrowIfInvalid();
    }
}
