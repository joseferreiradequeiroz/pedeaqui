namespace PedeAqui.Application.Idempotency;

public class IdempotencyOptions
{
    public const string SECTION = "Idempotency";

    public int KeyTtlHours { get; set; }
    public int InProgressLockSeconds { get; set; }
}
