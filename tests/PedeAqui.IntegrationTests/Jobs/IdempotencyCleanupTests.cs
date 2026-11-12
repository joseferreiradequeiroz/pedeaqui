using PedeAqui.Application.Idempotency;
using PedeAqui.Application.UseCases.Idempotency.Cleanup;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests.Jobs;

[Collection(IntegrationCollection.Name)]
public class IdempotencyCleanupTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private readonly PedeAquiWebApplicationFactory _factory;

    public IdempotencyCleanupTests(PedeAquiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Cleanup_deletes_only_expired_keys()
    {
        await using var jobs = await JobsHarness.Create(_factory);
        var userId = await _factory.UserIdByEmail(PedeAquiWebApplicationFactory.ADMIN_EMAIL);
        var now = jobs.Clock.GetUtcNow().UtcDateTime;
        var expiredKey = PedeAquiApiClient.NewIdempotencyKey();
        var expiredInProgressKey = PedeAquiApiClient.NewIdempotencyKey();
        var validKey = PedeAquiApiClient.NewIdempotencyKey();
        await _factory.AddIdempotencyRequest(KeyExpiringAt(userId, expiredKey, now.AddHours(-1), IdempotencyStatus.Completed));
        await _factory.AddIdempotencyRequest(KeyExpiringAt(userId, expiredInProgressKey, now.AddMinutes(-5), IdempotencyStatus.InProgress));
        await _factory.AddIdempotencyRequest(KeyExpiringAt(userId, validKey, now.AddHours(1), IdempotencyStatus.Completed));

        var deleted = await jobs.Run<ICleanupIdempotencyKeysUseCase>();

        Assert.True(deleted >= 2);
        Assert.Null(await _factory.IdempotencyRequestOf(userId, expiredKey));
        Assert.Null(await _factory.IdempotencyRequestOf(userId, expiredInProgressKey));
        Assert.NotNull(await _factory.IdempotencyRequestOf(userId, validKey));
    }

    [Fact]
    public async Task Cleanup_deletes_key_once_the_clock_passes_its_expiration()
    {
        await using var jobs = await JobsHarness.Create(_factory);
        var userId = await _factory.UserIdByEmail(PedeAquiWebApplicationFactory.ADMIN_EMAIL);
        var key = PedeAquiApiClient.NewIdempotencyKey();
        var now = jobs.Clock.GetUtcNow().UtcDateTime;
        await _factory.AddIdempotencyRequest(KeyExpiringAt(userId, key, now.AddHours(24), IdempotencyStatus.Completed));

        await jobs.Run<ICleanupIdempotencyKeysUseCase>();
        Assert.NotNull(await _factory.IdempotencyRequestOf(userId, key));

        jobs.Clock.Advance(TimeSpan.FromHours(25));
        await jobs.Run<ICleanupIdempotencyKeysUseCase>();

        Assert.Null(await _factory.IdempotencyRequestOf(userId, key));
    }

    private static IdempotencyRequest KeyExpiringAt(long userId, string key, DateTime expiresAt, IdempotencyStatus status)
    {
        const string BODY = "{}";

        return new IdempotencyRequest
        {
            UserId = userId,
            Key = key,
            Method = HttpMethod.Post.Method,
            Path = "/api/orders",
            RequestBody = BODY,
            RequestHash = IdempotencyRequestHash.Of(BODY),
            Status = status,
            CreatedAt = expiresAt.AddHours(-24),
            ExpiresAt = expiresAt,
            LockedUntil = expiresAt.AddHours(-24)
        };
    }
}
