using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Products.Delete;

public class DeleteProductUseCase : IDeleteProductUseCase
{
    private readonly IProductUpdateOnlyRepository _updateOnlyRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteProductUseCase(IProductUpdateOnlyRepository updateOnlyRepository, IUnitOfWork unitOfWork)
    {
        _updateOnlyRepository = updateOnlyRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Execute(long id)
    {
        var product = await _updateOnlyRepository.GetById(id)
            ?? throw new NotFoundException(ResourceErrorMessages.PRODUCT_NOT_FOUND);

        product.Deactivate();

        _updateOnlyRepository.Update(product);
        await _unitOfWork.Commit();
    }
}
