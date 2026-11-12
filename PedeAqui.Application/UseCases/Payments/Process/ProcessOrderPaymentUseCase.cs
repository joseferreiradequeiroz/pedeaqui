using Microsoft.Extensions.Logging;
using PedeAqui.Application.Services;
using PedeAqui.Domain.Caching;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Domain.Payments;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Inbox;
using PedeAqui.Domain.Repositories.Orders;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Payments.Process;

public class ProcessOrderPaymentUseCase : IProcessOrderPaymentUseCase
{
    public const string CONSUMER = "payments";

    private readonly IOrderUpdateOnlyRepository _orderRepository;
    private readonly IInboxRepository _inboxRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IOrderStockReleaser _stockReleaser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductCacheInvalidator _cacheInvalidator;
    private readonly ILogger<ProcessOrderPaymentUseCase> _logger;

    public ProcessOrderPaymentUseCase(
        IOrderUpdateOnlyRepository orderRepository,
        IInboxRepository inboxRepository,
        IPaymentGateway paymentGateway,
        IOrderStockReleaser stockReleaser,
        IUnitOfWork unitOfWork,
        IProductCacheInvalidator cacheInvalidator,
        ILogger<ProcessOrderPaymentUseCase> logger)
    {
        _orderRepository = orderRepository;
        _inboxRepository = inboxRepository;
        _paymentGateway = paymentGateway;
        _stockReleaser = stockReleaser;
        _unitOfWork = unitOfWork;
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;
    }

    public static string PaymentKeyOf(long orderId) => $"order-{orderId}";

    public async Task<PaymentProcessingOutcome> Execute(Guid messageId, long orderId, CancellationToken cancellationToken)
    {
        if (await _inboxRepository.Exists(messageId, CONSUMER, cancellationToken))
            return PaymentProcessingOutcome.Skipped;

        var orderAwaitingPayment = await MoveToAwaitingPayment(messageId, orderId, cancellationToken);
        if (orderAwaitingPayment is null)
            return PaymentProcessingOutcome.Skipped;

        var paymentKey = PaymentKeyOf(orderAwaitingPayment.Id);
        var payment = await _paymentGateway.Charge(paymentKey, paymentKey, orderAwaitingPayment.Total, cancellationToken);

        var settlement = await _unitOfWork.ExecuteInTransaction(
            transactionCancellationToken => ApplyPaymentResult(messageId, orderId, payment, transactionCancellationToken),
            cancellationToken);

        if (settlement.ReleasedProductIds.Count > 0)
            await _cacheInvalidator.Invalidate(settlement.ReleasedProductIds, CancellationToken.None);

        return settlement.Outcome;
    }

    private async Task<Order?> MoveToAwaitingPayment(Guid messageId, long orderId, CancellationToken cancellationToken)
    {
        try
        {
            return await _unitOfWork.ExecuteInTransaction(
                transactionCancellationToken => StartPaymentUnlessFinal(messageId, orderId, transactionCancellationToken),
                cancellationToken);
        }
        catch (ConflictException)
        {
            return await _unitOfWork.ExecuteInTransaction(
                transactionCancellationToken => StartPaymentUnlessFinal(messageId, orderId, transactionCancellationToken),
                cancellationToken);
        }
    }

    private async Task<Order?> StartPaymentUnlessFinal(Guid messageId, long orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrder(orderId, cancellationToken);

        if (order.IsFinal)
        {
            await RecordInInbox(messageId, cancellationToken);
            return null;
        }

        if (order.Status == OrderStatus.Pending)
        {
            order.StartPayment();
            _orderRepository.Update(order);
        }

        return order;
    }

    private async Task<PaymentSettlement> ApplyPaymentResult(Guid messageId, long orderId, PaymentResult payment, CancellationToken cancellationToken)
    {
        var order = await LoadOrder(orderId, cancellationToken);

        await RecordInInbox(messageId, cancellationToken);

        if (order.IsFinal)
            return SettlementForFinalOrder(order, payment);

        if (payment.Status == PaymentStatus.Approved)
        {
            order.ConfirmPayment();
            _orderRepository.Update(order);
            return new PaymentSettlement(PaymentProcessingOutcome.Confirmed, []);
        }

        order.FailPayment();
        await _stockReleaser.Release(order, $"Pagamento recusado do pedido #{order.Id}", cancellationToken);
        _orderRepository.Update(order);

        return new PaymentSettlement(PaymentProcessingOutcome.Compensated, order.Items.Select(item => item.ProductId).ToList());
    }

    private PaymentSettlement SettlementForFinalOrder(Order order, PaymentResult payment)
    {
        if (payment.Status != PaymentStatus.Approved || order.Status == OrderStatus.Confirmed)
            return new PaymentSettlement(PaymentProcessingOutcome.Skipped, []);

        _logger.LogError(
            "Payment {PaymentId} was approved after order {OrderId} became {OrderStatus}; the charge must be refunded",
            payment.PaymentId,
            order.Id,
            order.Status);

        return new PaymentSettlement(PaymentProcessingOutcome.ChargedAfterCancellation, []);
    }

    private async Task<Order> LoadOrder(long orderId, CancellationToken cancellationToken)
    {
        return await _orderRepository.GetByIdForSystem(orderId, cancellationToken)
            ?? throw new NotFoundException(ResourceErrorMessages.ORDER_NOT_FOUND);
    }

    private Task RecordInInbox(Guid messageId, CancellationToken cancellationToken)
    {
        return _inboxRepository.Add(new InboxMessage
        {
            MessageId = messageId,
            Consumer = CONSUMER,
            ProcessedAt = DateTime.UtcNow
        }, cancellationToken);
    }

    private sealed record PaymentSettlement(PaymentProcessingOutcome Outcome, IReadOnlyCollection<long> ReleasedProductIds);
}
