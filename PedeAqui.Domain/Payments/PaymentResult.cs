namespace PedeAqui.Domain.Payments;

public enum PaymentStatus
{
    Approved,
    Declined
}

public record PaymentResult(PaymentStatus Status, string PaymentId);
