using PedeAqui.Domain.Payments;

namespace PedeAqui.UnitTests.Fakes;

public sealed record ChargeRequest(string IdempotencyKey, string OrderReference, decimal Amount);

public sealed class FakePaymentGateway : IPaymentGateway
{
    private readonly Queue<System.Exception> _failuresForNextCharges = new();

    public PaymentStatus StatusToAnswer { get; set; } = PaymentStatus.Approved;
    public List<ChargeRequest> Charges { get; } = [];
    public Action? WhenCharged { get; set; }
    public Dictionary<string, PaymentResult> PaymentsByKey { get; } = [];
    public System.Exception? FindFailure { get; set; }
    public Dictionary<string, System.Exception> FindFailuresByKey { get; } = [];
    public List<string> Lookups { get; } = [];
    public Action<string>? WhenFound { get; set; }

    public void FailNextChargeWith(System.Exception failure) => _failuresForNextCharges.Enqueue(failure);

    public Task<PaymentResult> Charge(string idempotencyKey, string orderReference, decimal amount, CancellationToken cancellationToken)
    {
        Charges.Add(new ChargeRequest(idempotencyKey, orderReference, amount));
        WhenCharged?.Invoke();

        if (_failuresForNextCharges.TryDequeue(out var failure))
            throw failure;

        return Task.FromResult(new PaymentResult(StatusToAnswer, $"payment-{idempotencyKey}"));
    }

    public Task<PaymentResult?> Find(string idempotencyKey, CancellationToken cancellationToken)
    {
        Lookups.Add(idempotencyKey);

        if (FindFailure is not null)
            throw FindFailure;

        if (FindFailuresByKey.TryGetValue(idempotencyKey, out var keyFailure))
            throw keyFailure;

        WhenFound?.Invoke(idempotencyKey);

        return Task.FromResult(PaymentsByKey.GetValueOrDefault(idempotencyKey));
    }
}
