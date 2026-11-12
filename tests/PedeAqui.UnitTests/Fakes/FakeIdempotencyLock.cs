using PedeAqui.Domain.Services;

namespace PedeAqui.UnitTests.Fakes;

public sealed class FakeIdempotencyLock : IIdempotencyLock
{
    private readonly bool? _acquireOutcome;

    public FakeIdempotencyLock(bool? acquireOutcome = true)
    {
        _acquireOutcome = acquireOutcome;
    }

    public List<(string LockKey, TimeSpan Ttl)> AcquireAttempts { get; } = [];
    public List<string> ReleasedKeys { get; } = [];

    public Task<bool?> TryAcquire(string lockKey, TimeSpan ttl, CancellationToken cancellationToken)
    {
        AcquireAttempts.Add((lockKey, ttl));
        return Task.FromResult(_acquireOutcome);
    }

    public Task Release(string lockKey)
    {
        ReleasedKeys.Add(lockKey);
        return Task.CompletedTask;
    }
}
