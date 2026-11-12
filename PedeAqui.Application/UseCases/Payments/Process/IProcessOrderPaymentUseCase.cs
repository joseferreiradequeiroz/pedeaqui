namespace PedeAqui.Application.UseCases.Payments.Process;

public interface IProcessOrderPaymentUseCase
{
    Task<PaymentProcessingOutcome> Execute(Guid messageId, long orderId, CancellationToken cancellationToken);
}
