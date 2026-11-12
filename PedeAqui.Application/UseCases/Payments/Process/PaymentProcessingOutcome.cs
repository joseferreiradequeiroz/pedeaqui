namespace PedeAqui.Application.UseCases.Payments.Process;

public enum PaymentProcessingOutcome
{
    Confirmed,
    Compensated,
    Skipped,
    ChargedAfterCancellation
}
