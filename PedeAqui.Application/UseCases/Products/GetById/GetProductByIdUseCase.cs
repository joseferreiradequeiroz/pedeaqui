using AutoMapper;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Products.GetById;

public class GetProductByIdUseCase : IGetProductByIdUseCase
{
    private readonly IProductReadOnlyRepository _readOnlyRepository;
    private readonly IMapper _mapper;

    public GetProductByIdUseCase(IProductReadOnlyRepository readOnlyRepository, IMapper mapper)
    {
        _readOnlyRepository = readOnlyRepository;
        _mapper = mapper;
    }

    public async Task<ProductResponse> Execute(long id)
    {
        var product = await _readOnlyRepository.GetById(id)
            ?? throw new NotFoundException(ResourceErrorMessages.PRODUCT_NOT_FOUND);

        return _mapper.Map<ProductResponse>(product);
    }
}
