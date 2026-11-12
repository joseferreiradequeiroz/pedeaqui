namespace PedeAqui.Domain.Services;

public interface IRateLimiter
{
    Task<RateLimitDecision> Acquire(string bucket, int limit, TimeSpan window, CancellationToken cancellationToken);
}

public record RateLimitDecision(bool Allowed, TimeSpan RetryAfter);
