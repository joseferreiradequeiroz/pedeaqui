namespace PedeAqui.Application.Idempotency;

public abstract record IdempotencyStart
{
    public sealed record Started(long RequestId) : IdempotencyStart;

    public sealed record Replay(int StatusCode, string? Body) : IdempotencyStart;

    public sealed record InProgressConflict : IdempotencyStart;

    public sealed record PayloadMismatch : IdempotencyStart;
}
