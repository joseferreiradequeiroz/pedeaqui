namespace PedeAqui.Domain.Payments;

public interface IPaymentGateway
{
    Task<PaymentResult> Charge(string idempotencyKey, string orderReference, decimal amount, CancellationToken cancellationToken);

    Task<PaymentResult?> Find(string idempotencyKey, CancellationToken cancellationToken);
}
