using PedeAqui.Domain.Caching;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Products.Delete;

public class DeleteProductUseCase : IDeleteProductUseCase
{
    private readonly IProductUpdateOnlyRepository _updateOnlyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductCacheInvalidator _cacheInvalidator;

    public DeleteProductUseCase(IProductUpdateOnlyRepository updateOnlyRepository, IUnitOfWork unitOfWork, IProductCacheInvalidator cacheInvalidator)
    {
        _updateOnlyRepository = updateOnlyRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
    }

    public async Task Execute(long id, CancellationToken cancellationToken)
    {
        var product = await _updateOnlyRepository.GetById(id, cancellationToken)
            ?? throw new NotFoundException(ResourceErrorMessages.PRODUCT_NOT_FOUND);

        product.Deactivate();

        _updateOnlyRepository.Update(product);
        await _unitOfWork.Commit(cancellationToken);
        await _cacheInvalidator.Invalidate([id], CancellationToken.None);
    }
}
