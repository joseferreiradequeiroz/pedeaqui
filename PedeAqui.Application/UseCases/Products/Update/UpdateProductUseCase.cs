using AutoMapper;
using FluentValidation.Results;
using PedeAqui.Application.Validation;
using PedeAqui.Communication.Requests;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Products.Update;

public class UpdateProductUseCase : IUpdateProductUseCase
{
    private readonly IProductUpdateOnlyRepository _updateOnlyRepository;
    private readonly IProductReadOnlyRepository _readOnlyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public UpdateProductUseCase(
        IProductUpdateOnlyRepository updateOnlyRepository,
        IProductReadOnlyRepository readOnlyRepository,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _updateOnlyRepository = updateOnlyRepository;
        _readOnlyRepository = readOnlyRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task Execute(long id, ProductRequest request)
    {
        await Validate(id, request);

        var product = await _updateOnlyRepository.GetById(id)
            ?? throw new NotFoundException(ResourceErrorMessages.PRODUCT_NOT_FOUND);

        _mapper.Map(request, product);

        _updateOnlyRepository.Update(product);
        await _unitOfWork.Commit();
    }

    private async Task Validate(long id, ProductRequest request)
    {
        var result = new ProductValidator().Validate(request);

        if (!string.IsNullOrWhiteSpace(request.Sku) && await _readOnlyRepository.ExistsWithSku(request.Sku, ignoreProductId: id))
            result.Errors.Add(new ValidationFailure(nameof(request.Sku), ResourceErrorMessages.PRODUCT_SKU_ALREADY_EXISTS));

        result.ThrowIfInvalid();
    }
}
