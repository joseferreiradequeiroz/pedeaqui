using System.Collections.Concurrent;

namespace PedeAqui.PaymentGateway.Payments;

public enum PaymentStatus
{
    Approved,
    Declined
}

public record Payment(Guid PaymentId, PaymentStatus Status, decimal Amount);

public class PaymentStore
{
    private const int DeclinedCents = 13;

    private readonly ConcurrentDictionary<string, Lazy<Payment>> paymentsByKey = new();

    public Payment Process(string idempotencyKey, decimal amount) =>
        paymentsByKey.GetOrAdd(idempotencyKey, _ => new Lazy<Payment>(() => Decide(amount))).Value;

    public Payment? Find(string idempotencyKey) =>
        paymentsByKey.TryGetValue(idempotencyKey, out var payment) ? payment.Value : null;

    private static Payment Decide(decimal amount)
    {
        var cents = (int)(decimal.Round(amount * 100) % 100);
        var status = cents == DeclinedCents ? PaymentStatus.Declined : PaymentStatus.Approved;
        return new Payment(Guid.NewGuid(), status, amount);
    }
}
