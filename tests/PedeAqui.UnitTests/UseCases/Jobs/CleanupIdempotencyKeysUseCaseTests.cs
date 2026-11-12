using Microsoft.Extensions.Time.Testing;
using PedeAqui.Application.UseCases.Idempotency.Cleanup;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.UnitTests.Fakes;

namespace PedeAqui.UnitTests.UseCases.Jobs;

public class CleanupIdempotencyKeysUseCaseTests
{
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    [Fact]
    public async Task Deletes_only_keys_past_their_expiration()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var expiredCompleted = KeyExpiringAt("expired-completed", now.AddMinutes(-1), IdempotencyStatus.Completed);
        var expiredInProgress = KeyExpiringAt("expired-in-progress", now.AddHours(-3), IdempotencyStatus.InProgress);
        var stillValid = KeyExpiringAt("still-valid", now.AddMinutes(1), IdempotencyStatus.Completed);
        var repository = new InMemoryIdempotencyRepository(expiredCompleted, expiredInProgress, stillValid);

        var deleted = await new CleanupIdempotencyKeysUseCase(repository, _clock).Execute(CancellationToken.None);

        Assert.Equal(2, deleted);
        Assert.Equal([stillValid], repository.Requests);
    }

    [Fact]
    public async Task Deletes_key_once_the_clock_passes_its_expiration()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var key = KeyExpiringAt("soon", now.AddHours(24), IdempotencyStatus.Completed);
        var repository = new InMemoryIdempotencyRepository(key);
        var useCase = new CleanupIdempotencyKeysUseCase(repository, _clock);

        Assert.Equal(0, await useCase.Execute(CancellationToken.None));

        _clock.Advance(TimeSpan.FromHours(25));

        Assert.Equal(1, await useCase.Execute(CancellationToken.None));
        Assert.Empty(repository.Requests);
    }

    private static IdempotencyRequest KeyExpiringAt(string key, DateTime expiresAt, IdempotencyStatus status) => new()
    {
        UserId = 1,
        Key = key,
        Status = status,
        CreatedAt = expiresAt.AddHours(-24),
        ExpiresAt = expiresAt,
        LockedUntil = expiresAt.AddHours(-24)
    };
}
