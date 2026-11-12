using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedeAqui.Application.Services;
using PedeAqui.Application.Settings;
using PedeAqui.Application.UseCases.Payments.Process;
using PedeAqui.Domain.Caching;
using PedeAqui.Domain.Payments;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Orders;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Payments.Reconcile;

public class ReconcilePaymentsUseCase : IReconcilePaymentsUseCase
{
    private readonly IOrderUpdateOnlyRepository _orderRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IOrderStockReleaser _stockReleaser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductCacheInvalidator _cacheInvalidator;
    private readonly TimeProvider _timeProvider;
    private readonly PaymentsOptions _options;
    private readonly ILogger<ReconcilePaymentsUseCase> _logger;

    public ReconcilePaymentsUseCase(
        IOrderUpdateOnlyRepository orderRepository,
        IPaymentGateway paymentGateway,
        IOrderStockReleaser stockReleaser,
        IUnitOfWork unitOfWork,
        IProductCacheInvalidator cacheInvalidator,
        TimeProvider timeProvider,
        IOptions<PaymentsOptions> options,
        ILogger<ReconcilePaymentsUseCase> logger)
    {
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
        _stockReleaser = stockReleaser;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> Execute(CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-_options.ReconcileAfterMinutes);
        var reconciled = 0;

        while (true)
        {
            var orderIds = await _orderRepository.GetAwaitingPaymentStartedBefore(cutoff, _options.ReconcileBatchSize, cancellationToken);

            var reconciledInBatch = 0;
            foreach (var orderId in orderIds)
            {
                PaymentResult? payment;

                try
                {
                    payment = await _paymentGateway.Find(ProcessOrderPaymentUseCase.PaymentKeyOf(orderId), cancellationToken);
                }
                catch (PaymentGatewayUnavailableException)
                {
                    return reconciled + reconciledInBatch;
                }
                catch (PaymentGatewayRejectedException exception)
                {
                    _logger.LogError(
                        exception,
                        "Payment gateway rejected the lookup of order {OrderId} with {GatewayStatusCode}; skipping it in this cycle",
                        orderId,
                        exception.GatewayStatusCode);
                    continue;
                }

                if (await TryReconcile(orderId, payment, cancellationToken))
                    reconciledInBatch++;
            }

            reconciled += reconciledInBatch;

            if (orderIds.Count < _options.ReconcileBatchSize || reconciledInBatch == 0)
                return reconciled;
        }
    }

    private async Task<bool> TryReconcile(long orderId, PaymentResult? payment, CancellationToken cancellationToken)
    {
        Settlement? settlement;

        try
        {
            settlement = await _unitOfWork.ExecuteInTransaction(
                transactionCancellationToken => Settle(orderId, payment, transactionCancellationToken),
                cancellationToken);
        }
        catch (ConflictException)
        {
            return false;
        }

        if (settlement is null)
            return false;

        if (settlement.ReleasedProductIds.Count > 0)
            await _cacheInvalidator.Invalidate(settlement.ReleasedProductIds, CancellationToken.None);

        return true;
    }

    private async Task<Settlement?> Settle(long orderId, PaymentResult? payment, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdForSystem(orderId, cancellationToken);
        if (order is null)
            return null;

        if (payment?.Status == PaymentStatus.Approved)
        {
            order.ConfirmPayment();
            _orderRepository.Update(order);
            return new Settlement([]);
        }

        order.FailPayment();
        await _stockReleaser.Release(order, $"Pagamento não aprovado do pedido #{order.Id}", cancellationToken);
        _orderRepository.Update(order);

        return new Settlement(order.Items.Select(item => item.ProductId).ToList());
    }

    private sealed record Settlement(IReadOnlyCollection<long> ReleasedProductIds);
}
