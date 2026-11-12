using PedeAqui.Application.Services;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Caching;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Orders;
using PedeAqui.Domain.Services;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Orders.Cancel;

public class CancelOrderUseCase : ICancelOrderUseCase
{
    private readonly IOrderUpdateOnlyRepository _orderRepository;
    private readonly IOrderStockReleaser _stockReleaser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductCacheInvalidator _cacheInvalidator;
    private readonly ILoggedUser _loggedUser;

    public CancelOrderUseCase(
        IOrderUpdateOnlyRepository orderRepository,
        IOrderStockReleaser stockReleaser,
        IUnitOfWork unitOfWork,
        IProductCacheInvalidator cacheInvalidator,
        ILoggedUser loggedUser)
    {
        _orderRepository = orderRepository;
        _stockReleaser = stockReleaser;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
        _loggedUser = loggedUser;
    }

    public async Task Execute(long id, CancellationToken cancellationToken)
    {
        var user = await _loggedUser.Get(cancellationToken);

        var productIds = await _unitOfWork.ExecuteInTransaction(
            transactionCancellationToken => CancelOrder(user, id, transactionCancellationToken),
            cancellationToken);
        await _cacheInvalidator.Invalidate(productIds, CancellationToken.None);
    }

    private async Task<IReadOnlyCollection<long>> CancelOrder(User user, long id, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetById(user, id, cancellationToken)
            ?? throw new NotFoundException(ResourceErrorMessages.ORDER_NOT_FOUND);

        order.Cancel();

        await _stockReleaser.Release(order, $"Cancelamento do pedido #{order.Id}", cancellationToken);

        _orderRepository.Update(order);

        return order.Items.Select(item => item.ProductId).ToList();
    }
}
