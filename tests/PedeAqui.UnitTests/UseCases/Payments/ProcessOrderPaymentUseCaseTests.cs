using Microsoft.Extensions.Logging.Abstractions;
using PedeAqui.Application.Services;
using PedeAqui.Application.UseCases.Payments.Process;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Domain.Payments;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Fakes;
using PedeAqui.UnitTests.Support;

namespace PedeAqui.UnitTests.UseCases.Payments;

public class ProcessOrderPaymentUseCaseTests
{
    private readonly User _customer = UserBuilder.Build(id: 1);
    private readonly Product _pizza = ProductBuilder.Build(id: 1, stock: 10, price: 40m);
    private readonly InMemoryStockMovementRepository _movements = new();
    private readonly InMemoryInboxRepository _inbox = new();
    private readonly FakePaymentGateway _gateway = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeProductCacheInvalidator _cache;
    private readonly Guid _messageId = Guid.NewGuid();

    public ProcessOrderPaymentUseCaseTests()
    {
        _cache = new FakeProductCacheInvalidator(_unitOfWork);
    }

    [Fact]
    public async Task Approved_confirms_order()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 2));
        _gateway.StatusToAnswer = PaymentStatus.Approved;

        var outcome = await UseCaseFor(order).Execute(_messageId, order.Id, CancellationToken.None);

        Assert.Equal(PaymentProcessingOutcome.Confirmed, outcome);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.NotNull(order.PaymentStartedAt);
        var charge = Assert.Single(_gateway.Charges);
        Assert.Equal(new ChargeRequest($"order-{order.Id}", $"order-{order.Id}", 80m), charge);
        Assert.Equal(8, _pizza.StockQuantity);
        AssertInboxRecordedOnce();
        Assert.Equal(0, _cache.InvalidationCount);
    }

    [Fact]
    public async Task Declined_cancels_and_releases_stock()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 2));
        _gateway.StatusToAnswer = PaymentStatus.Declined;

        var outcome = await UseCaseFor(order).Execute(_messageId, order.Id, CancellationToken.None);

        Assert.Equal(PaymentProcessingOutcome.Compensated, outcome);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(10, _pizza.StockQuantity);
        var returned = Assert.Single(_movements.Movements);
        Assert.Equal(StockMovementType.Entry, returned.Type);
        Assert.Equal(2, returned.Quantity);
        Assert.Equal($"Pagamento recusado do pedido #{order.Id}", returned.Reason);
        AssertInboxRecordedOnce();
        Assert.Equal([_pizza.Id], _cache.InvalidatedProductIds);
        Assert.Equal(_unitOfWork.CommitCount, _cache.CommitCountAtLastInvalidation);
    }

    [Fact]
    public async Task Duplicate_message_is_skipped()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 2));
        await _inbox.Add(new InboxMessage { MessageId = _messageId, Consumer = ProcessOrderPaymentUseCase.CONSUMER, ProcessedAt = DateTime.UtcNow }, CancellationToken.None);

        var outcome = await UseCaseFor(order).Execute(_messageId, order.Id, CancellationToken.None);

        Assert.Equal(PaymentProcessingOutcome.Skipped, outcome);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Empty(_gateway.Charges);
        Assert.Equal(0, _unitOfWork.CommitCount);
        AssertInboxRecordedOnce();
    }

    [Fact]
    public async Task Final_order_is_skipped()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 2));
        order.Cancel();

        var outcome = await UseCaseFor(order).Execute(_messageId, order.Id, CancellationToken.None);

        Assert.Equal(PaymentProcessingOutcome.Skipped, outcome);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Empty(_gateway.Charges);
        Assert.Empty(_movements.Movements);
        AssertInboxRecordedOnce();
    }

    [Fact]
    public async Task Approved_charge_after_the_reconciler_cancelled_the_order_is_flagged_for_refund()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 2));
        _gateway.StatusToAnswer = PaymentStatus.Approved;
        _gateway.WhenCharged = () => order.FailPayment();

        var outcome = await UseCaseFor(order).Execute(_messageId, order.Id, CancellationToken.None);

        Assert.Equal(PaymentProcessingOutcome.ChargedAfterCancellation, outcome);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Single(_gateway.Charges);
        AssertInboxRecordedOnce();
        Assert.Equal(0, _cache.InvalidationCount);
    }

    [Fact]
    public async Task Declined_charge_after_the_order_became_final_is_skipped()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 2));
        _gateway.StatusToAnswer = PaymentStatus.Declined;
        _gateway.WhenCharged = () => order.FailPayment();

        var outcome = await UseCaseFor(order).Execute(_messageId, order.Id, CancellationToken.None);

        Assert.Equal(PaymentProcessingOutcome.Skipped, outcome);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        AssertInboxRecordedOnce();
    }

    [Fact]
    public async Task Redelivery_while_awaiting_payment_charges_with_same_key()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 2));
        var useCase = UseCaseFor(order);
        _gateway.FailNextChargeWith(new PaymentGatewayUnavailableException(null));

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() => useCase.Execute(_messageId, order.Id, CancellationToken.None));
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Empty(_inbox.Messages);

        var outcome = await useCase.Execute(_messageId, order.Id, CancellationToken.None);

        Assert.Equal(PaymentProcessingOutcome.Confirmed, outcome);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(2, _gateway.Charges.Count);
        Assert.All(_gateway.Charges, charge => Assert.Equal($"order-{order.Id}", charge.IdempotencyKey));
        AssertInboxRecordedOnce();
    }

    [Fact]
    public async Task Order_is_committed_as_awaiting_payment_before_the_gateway_is_called()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 2));
        OrderStatus? statusWhenCharged = null;
        var commitsWhenCharged = 0;
        _gateway.WhenCharged = () =>
        {
            statusWhenCharged = order.Status;
            commitsWhenCharged = _unitOfWork.CommitCount;
        };

        await UseCaseFor(order).Execute(_messageId, order.Id, CancellationToken.None);

        Assert.Equal(OrderStatus.AwaitingPayment, statusWhenCharged);
        Assert.Equal(1, commitsWhenCharged);
    }

    [Fact]
    public async Task Gateway_rejection_propagates_and_leaves_order_awaiting_payment()
    {
        var order = OrderFactory.PendingWith(_customer, (_pizza, 2));
        _gateway.FailNextChargeWith(new PaymentGatewayRejectedException(System.Net.HttpStatusCode.UnprocessableEntity));

        await Assert.ThrowsAsync<PaymentGatewayRejectedException>(() => UseCaseFor(order).Execute(_messageId, order.Id, CancellationToken.None));

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Empty(_inbox.Messages);
    }

    private ProcessOrderPaymentUseCase UseCaseFor(Order order)
    {
        var products = new InMemoryProductRepository(_pizza);

        return new ProcessOrderPaymentUseCase(
            new InMemoryOrderRepository(order),
            _inbox,
            _gateway,
            new OrderStockReleaser(products, _movements),
            _unitOfWork,
            _cache,
            NullLogger<ProcessOrderPaymentUseCase>.Instance);
    }

    private void AssertInboxRecordedOnce()
    {
        var recorded = Assert.Single(_inbox.Messages);
        Assert.Equal(_messageId, recorded.MessageId);
        Assert.Equal(ProcessOrderPaymentUseCase.CONSUMER, recorded.Consumer);
    }
}
