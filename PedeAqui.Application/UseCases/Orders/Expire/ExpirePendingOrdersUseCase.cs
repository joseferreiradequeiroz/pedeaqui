using Microsoft.Extensions.Options;
using PedeAqui.Application.Services;
using PedeAqui.Application.Settings;
using PedeAqui.Domain.Caching;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Orders;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Orders.Expire;

public class ExpirePendingOrdersUseCase : IExpirePendingOrdersUseCase
{
    private readonly IOrderUpdateOnlyRepository _orderRepository;
    private readonly IOrderStockReleaser _stockReleaser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductCacheInvalidator _cacheInvalidator;
    private readonly TimeProvider _timeProvider;
    private readonly OrdersOptions _options;

    public ExpirePendingOrdersUseCase(
        IOrderUpdateOnlyRepository orderRepository,
        IOrderStockReleaser stockReleaser,
        IUnitOfWork unitOfWork,
        IProductCacheInvalidator cacheInvalidator,
        TimeProvider timeProvider,
        IOptions<OrdersOptions> options)
    {
        _orderRepository = orderRepository;
        _stockReleaser = stockReleaser;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public async Task<int> Execute(CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-_options.PendingExpirationMinutes);
        var expired = 0;

        while (true)
        {
            var orderIds = await _orderRepository.GetPendingCreatedBefore(cutoff, _options.ExpirationBatchSize, cancellationToken);

            var expiredInBatch = 0;
            foreach (var orderId in orderIds)
            {
                if (await TryExpire(orderId, cancellationToken))
                    expiredInBatch++;
            }

            expired += expiredInBatch;

            if (orderIds.Count < _options.ExpirationBatchSize || expiredInBatch == 0)
                return expired;
        }
    }

    private async Task<bool> TryExpire(long orderId, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<long>? productIds;

        try
        {
            productIds = await _unitOfWork.ExecuteInTransaction(
                transactionCancellationToken => ExpireOrder(orderId, transactionCancellationToken),
                cancellationToken);
        }
        catch (ConflictException)
        {
            return false;
        }

        if (productIds is null)
            return false;

        await _cacheInvalidator.Invalidate(productIds, CancellationToken.None);

        return true;
    }

    private async Task<IReadOnlyCollection<long>?> ExpireOrder(long orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdForSystem(orderId, cancellationToken);
        if (order is null)
            return null;

        order.Expire();

        await _stockReleaser.Release(order, $"Expiração do pedido #{order.Id}", cancellationToken);

        _orderRepository.Update(order);

        return order.Items.Select(item => item.ProductId).ToList();
    }
}
