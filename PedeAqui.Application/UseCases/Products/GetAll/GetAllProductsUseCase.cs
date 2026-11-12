using AutoMapper;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Repositories.Products;

namespace PedeAqui.Application.UseCases.Products.GetAll;

public class GetAllProductsUseCase : IGetAllProductsUseCase
{
    private readonly IProductReadOnlyRepository _readOnlyRepository;
    private readonly IMapper _mapper;

    public GetAllProductsUseCase(IProductReadOnlyRepository readOnlyRepository, IMapper mapper)
    {
        _readOnlyRepository = readOnlyRepository;
        _mapper = mapper;
    }

    public async Task<ProductsResponse> Execute(CancellationToken cancellationToken)
    {
        var products = await _readOnlyRepository.GetAllActive(cancellationToken);

        return new ProductsResponse
        {
            Products = _mapper.Map<List<ProductResponse>>(products)
        };
    }
}
