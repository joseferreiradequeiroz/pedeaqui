using Microsoft.Extensions.Options;
using PedeAqui.Application.Idempotency;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;
using PedeAqui.UnitTests.Fakes;

namespace PedeAqui.UnitTests.Idempotency;

public class IdempotencyServiceTests
{
    private const long USER_ID = 7;
    private const string KEY = "chave-1";
    private const string METHOD = "POST";
    private const string PATH = "/api/orders";
    private const string BODY = """{"items":[{"productId":1,"quantity":2}]}""";
    private const string LOCK_KEY = "idem:7:chave-1";
    private const int KEY_TTL_HOURS = 24;
    private const int IN_PROGRESS_LOCK_SECONDS = 60;

    [Fact]
    public async Task Begin_NewKey_InsertsInProgressRowAndStarts()
    {
        var repository = new InMemoryIdempotencyRepository();
        var service = Service(repository, new FakeIdempotencyLock());
        var before = DateTime.UtcNow;

        var start = await service.Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        var started = Assert.IsType<IdempotencyStart.Started>(start);
        var stored = Assert.Single(repository.Requests);
        Assert.Equal(stored.Id, started.RequestId);
        Assert.Equal(IdempotencyStatus.InProgress, stored.Status);
        Assert.Equal(USER_ID, stored.UserId);
        Assert.Equal(KEY, stored.Key);
        Assert.Equal(METHOD, stored.Method);
        Assert.Equal(PATH, stored.Path);
        Assert.Equal(BODY, stored.RequestBody);
        Assert.Equal(IdempotencyRequestHash.Of(BODY), stored.RequestHash);
        Assert.InRange(stored.ExpiresAt - before, TimeSpan.FromHours(KEY_TTL_HOURS), TimeSpan.FromHours(KEY_TTL_HOURS).Add(TimeSpan.FromMinutes(1)));
        Assert.InRange(stored.LockedUntil - before, TimeSpan.FromSeconds(IN_PROGRESS_LOCK_SECONDS), TimeSpan.FromSeconds(IN_PROGRESS_LOCK_SECONDS + 60));
    }

    [Fact]
    public async Task Begin_AcquiresRedisLockForUserAndKeyWithInProgressTtl()
    {
        var idempotencyLock = new FakeIdempotencyLock();

        await Service(new InMemoryIdempotencyRepository(), idempotencyLock).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        var attempt = Assert.Single(idempotencyLock.AcquireAttempts);
        Assert.Equal(LOCK_KEY, attempt.LockKey);
        Assert.Equal(TimeSpan.FromSeconds(IN_PROGRESS_LOCK_SECONDS), attempt.Ttl);
        Assert.Empty(idempotencyLock.ReleasedKeys);
    }

    [Fact]
    public async Task Begin_RedisLockHeldByAnotherRequest_ReturnsInProgressConflictWithoutWritingToDatabase()
    {
        var repository = new InMemoryIdempotencyRepository();
        var idempotencyLock = new FakeIdempotencyLock(acquireOutcome: false);

        var start = await Service(repository, idempotencyLock).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        Assert.IsType<IdempotencyStart.InProgressConflict>(start);
        Assert.Empty(repository.Requests);
        Assert.Empty(idempotencyLock.ReleasedKeys);
    }

    [Fact]
    public async Task Begin_RedisLockStillHeldButKeyAlreadyCompleted_ReplaysStoredResponse()
    {
        var repository = new InMemoryIdempotencyRepository(Completed(201, """{"id":10}"""));
        var idempotencyLock = new FakeIdempotencyLock(acquireOutcome: false);

        var start = await Service(repository, idempotencyLock).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        var replay = Assert.IsType<IdempotencyStart.Replay>(start);
        Assert.Equal(201, replay.StatusCode);
        Assert.Equal("""{"id":10}""", replay.Body);
        Assert.Empty(idempotencyLock.ReleasedKeys);
    }

    [Fact]
    public async Task Begin_RedisLockStillHeldAndKeyInProgress_ReturnsInProgressConflict()
    {
        var repository = new InMemoryIdempotencyRepository(InProgress(lockedUntil: DateTime.UtcNow.AddSeconds(30)));

        var start = await Service(repository, new FakeIdempotencyLock(acquireOutcome: false)).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        Assert.IsType<IdempotencyStart.InProgressConflict>(start);
    }

    [Fact]
    public async Task Begin_DatabaseFailsAfterAcquiringRedisLock_ReleasesTheLockAndRethrows()
    {
        var databaseDown = new InvalidOperationException("database down");
        var repository = new InMemoryIdempotencyRepository { ReadFailure = databaseDown };
        var idempotencyLock = new FakeIdempotencyLock();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(repository, idempotencyLock).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None));

        Assert.Same(databaseDown, thrown);
        Assert.Equal([LOCK_KEY], idempotencyLock.ReleasedKeys);
    }

    [Fact]
    public async Task Begin_RedisUnavailable_FallsBackToDatabaseAndStarts()
    {
        var repository = new InMemoryIdempotencyRepository();

        var start = await Service(repository, new FakeIdempotencyLock(acquireOutcome: null)).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        Assert.IsType<IdempotencyStart.Started>(start);
        Assert.Single(repository.Requests);
    }

    [Fact]
    public async Task Begin_CompletedKeyWithSameBody_ReplaysStoredResponseAndReleasesLock()
    {
        var repository = new InMemoryIdempotencyRepository(Completed(201, """{"id":10}"""));
        var idempotencyLock = new FakeIdempotencyLock();

        var start = await Service(repository, idempotencyLock).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        var replay = Assert.IsType<IdempotencyStart.Replay>(start);
        Assert.Equal(201, replay.StatusCode);
        Assert.Equal("""{"id":10}""", replay.Body);
        Assert.Equal([LOCK_KEY], idempotencyLock.ReleasedKeys);
    }

    [Fact]
    public async Task Begin_SameJsonWithDifferentFormattingAndPropertyOrder_IsTreatedAsSameBody()
    {
        var repository = new InMemoryIdempotencyRepository(Completed(201, """{"id":10}"""));
        const string reformatted = """
            {
              "items": [ { "quantity": 2, "productId": 1 } ]
            }
            """;

        var start = await Service(repository, new FakeIdempotencyLock()).Begin(USER_ID, KEY, METHOD, PATH, reformatted, CancellationToken.None);

        Assert.IsType<IdempotencyStart.Replay>(start);
    }

    [Fact]
    public async Task Begin_SameKeyWithDifferentBody_ReturnsPayloadMismatchAndReleasesLock()
    {
        var repository = new InMemoryIdempotencyRepository(Completed(201, """{"id":10}"""));
        var idempotencyLock = new FakeIdempotencyLock();

        var start = await Service(repository, idempotencyLock).Begin(USER_ID, KEY, METHOD, PATH, """{"items":[{"productId":1,"quantity":3}]}""", CancellationToken.None);

        Assert.IsType<IdempotencyStart.PayloadMismatch>(start);
        Assert.Equal([LOCK_KEY], idempotencyLock.ReleasedKeys);
    }

    [Fact]
    public async Task Begin_SameKeyOnAnotherEndpoint_ReturnsPayloadMismatch()
    {
        var repository = new InMemoryIdempotencyRepository(Completed(201, """{"id":10}"""));

        var start = await Service(repository, new FakeIdempotencyLock()).Begin(USER_ID, KEY, METHOD, "/api/products/1/stock/entries", BODY, CancellationToken.None);

        Assert.IsType<IdempotencyStart.PayloadMismatch>(start);
    }

    [Fact]
    public async Task Begin_KeyInProgressWithLockStillValid_ReturnsInProgressConflict()
    {
        var repository = new InMemoryIdempotencyRepository(InProgress(lockedUntil: DateTime.UtcNow.AddSeconds(30)));
        var idempotencyLock = new FakeIdempotencyLock(acquireOutcome: null);

        var start = await Service(repository, idempotencyLock).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        Assert.IsType<IdempotencyStart.InProgressConflict>(start);
        Assert.Single(repository.Requests);
    }

    [Fact]
    public async Task Begin_KeyStuckInProgressWithExpiredLock_IsTakenOverAndStarts()
    {
        var stuck = InProgress(lockedUntil: DateTime.UtcNow.AddMinutes(-5));
        var repository = new InMemoryIdempotencyRepository(stuck);

        var start = await Service(repository, new FakeIdempotencyLock()).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        var started = Assert.IsType<IdempotencyStart.Started>(start);
        Assert.Equal(stuck.Id, started.RequestId);
        Assert.True(stuck.LockedUntil > DateTime.UtcNow.AddSeconds(IN_PROGRESS_LOCK_SECONDS - 10));
        Assert.Single(repository.Requests);
    }

    [Fact]
    public async Task Begin_InsertLosesRaceToCompletedRequest_RereadsAndReplays()
    {
        var repository = new InMemoryIdempotencyRepository
        {
            InsertedByConcurrentRequestBeforeOurs = Completed(201, """{"id":10}""")
        };

        var start = await Service(repository, new FakeIdempotencyLock(acquireOutcome: null)).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        var replay = Assert.IsType<IdempotencyStart.Replay>(start);
        Assert.Equal(201, replay.StatusCode);
        Assert.Single(repository.Requests);
    }

    [Fact]
    public async Task Begin_InsertLosesRaceToRequestStillInProgress_ReturnsInProgressConflict()
    {
        var repository = new InMemoryIdempotencyRepository
        {
            InsertedByConcurrentRequestBeforeOurs = InProgress(lockedUntil: DateTime.UtcNow.AddSeconds(30))
        };

        var start = await Service(repository, new FakeIdempotencyLock(acquireOutcome: null)).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        Assert.IsType<IdempotencyStart.InProgressConflict>(start);
        Assert.Single(repository.Requests);
    }

    [Fact]
    public async Task Complete_StoresResponseMarksCompletedAndReleasesLock()
    {
        var repository = new InMemoryIdempotencyRepository();
        var idempotencyLock = new FakeIdempotencyLock();
        var service = Service(repository, idempotencyLock);
        var started = (IdempotencyStart.Started)await service.Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        await service.Complete(started.RequestId, 201, """{"id":10}""", CancellationToken.None);

        var stored = Assert.Single(repository.Requests);
        Assert.Equal(IdempotencyStatus.Completed, stored.Status);
        Assert.Equal(201, stored.ResponseStatusCode);
        Assert.Equal("""{"id":10}""", stored.ResponseBody);
        Assert.NotNull(stored.CompletedAt);
        Assert.Equal([LOCK_KEY], idempotencyLock.ReleasedKeys);
    }

    [Fact]
    public async Task Release_DeletesInProgressRowAndReleasesLockSoTheKeyCanBeRetried()
    {
        var repository = new InMemoryIdempotencyRepository();
        var idempotencyLock = new FakeIdempotencyLock();
        var service = Service(repository, idempotencyLock);
        var started = (IdempotencyStart.Started)await service.Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        await service.Release(started.RequestId, CancellationToken.None);

        Assert.Empty(repository.Requests);
        Assert.Equal([LOCK_KEY], idempotencyLock.ReleasedKeys);
        var retry = await Service(repository, new FakeIdempotencyLock()).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);
        Assert.IsType<IdempotencyStart.Started>(retry);
    }

    [Fact]
    public async Task Complete_WhenRedisWasUnavailable_DoesNotReleaseAnyLock()
    {
        var idempotencyLock = new FakeIdempotencyLock(acquireOutcome: null);
        var service = Service(new InMemoryIdempotencyRepository(), idempotencyLock);
        var started = (IdempotencyStart.Started)await service.Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        await service.Complete(started.RequestId, 201, null, CancellationToken.None);

        Assert.Empty(idempotencyLock.ReleasedKeys);
    }

    [Fact]
    public async Task Begin_ExpiredCompletedKey_IsTreatedAsAbsentAndStartsFresh()
    {
        var expired = Completed(201, """{"id":10}""");
        expired.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        var repository = new InMemoryIdempotencyRepository(expired);

        var start = await Service(repository, new FakeIdempotencyLock()).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        var started = Assert.IsType<IdempotencyStart.Started>(start);
        var stored = Assert.Single(repository.Requests);
        Assert.Equal(started.RequestId, stored.Id);
        Assert.Equal(IdempotencyStatus.InProgress, stored.Status);
        Assert.True(stored.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Begin_ExpiredInProgressKeyEvenWithLockStillValid_IsTreatedAsAbsentAndStartsFresh()
    {
        var expired = InProgress(lockedUntil: DateTime.UtcNow.AddSeconds(30));
        expired.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        var repository = new InMemoryIdempotencyRepository(expired);

        var start = await Service(repository, new FakeIdempotencyLock()).Begin(USER_ID, KEY, METHOD, PATH, BODY, CancellationToken.None);

        var started = Assert.IsType<IdempotencyStart.Started>(start);
        var stored = Assert.Single(repository.Requests);
        Assert.Equal(started.RequestId, stored.Id);
        Assert.True(stored.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Begin_ExpiredCompletedKeyWithDifferentBody_StartsFreshInsteadOfPayloadMismatch()
    {
        var expired = Completed(201, """{"id":10}""");
        expired.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        var repository = new InMemoryIdempotencyRepository(expired);

        var start = await Service(repository, new FakeIdempotencyLock()).Begin(USER_ID, KEY, METHOD, PATH, """{"items":[{"productId":1,"quantity":3}]}""", CancellationToken.None);

        Assert.IsType<IdempotencyStart.Started>(start);
    }

    [Fact]
    public async Task Begin_BodyWithDuplicatedPropertyNames_ThrowsValidationErrorWithoutTouchingTheLock()
    {
        var idempotencyLock = new FakeIdempotencyLock();

        var thrown = await Assert.ThrowsAsync<ErrorOnValidationException>(() =>
            Service(new InMemoryIdempotencyRepository(), idempotencyLock).Begin(USER_ID, KEY, METHOD, PATH, """{"items":[],"items":[]}""", CancellationToken.None));

        Assert.Contains(ResourceErrorMessages.IDEMPOTENCY_REQUEST_BODY_INVALID, thrown.GetErrorMessages());
        Assert.Empty(idempotencyLock.AcquireAttempts);
    }

    [Fact]
    public void Hash_IsSha256HexOfCanonicalJson()
    {
        var hash = IdempotencyRequestHash.Of("""{ "b": 1, "a": [ { "d": true, "c": null } ] }""");

        Assert.Equal(IdempotencyRequestHash.Of("""{"a":[{"c":null,"d":true}],"b":1}"""), hash);
        Assert.Equal(64, hash.Length);
        Assert.NotEqual(IdempotencyRequestHash.Of("""{"a":[{"c":null,"d":false}],"b":1}"""), hash);
    }

    private static IdempotencyService Service(InMemoryIdempotencyRepository repository, FakeIdempotencyLock idempotencyLock)
    {
        return new IdempotencyService(repository, idempotencyLock, Options.Create(new IdempotencyOptions
        {
            KeyTtlHours = KEY_TTL_HOURS,
            InProgressLockSeconds = IN_PROGRESS_LOCK_SECONDS
        }));
    }

    private static IdempotencyRequest Completed(int statusCode, string? body)
    {
        var request = InProgress(lockedUntil: DateTime.UtcNow);
        request.Status = IdempotencyStatus.Completed;
        request.ResponseStatusCode = statusCode;
        request.ResponseBody = body;
        request.CompletedAt = DateTime.UtcNow;
        return request;
    }

    private static IdempotencyRequest InProgress(DateTime lockedUntil)
    {
        return new IdempotencyRequest
        {
            UserId = USER_ID,
            Key = KEY,
            Method = METHOD,
            Path = PATH,
            RequestBody = BODY,
            RequestHash = IdempotencyRequestHash.Of(BODY),
            Status = IdempotencyStatus.InProgress,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            ExpiresAt = DateTime.UtcNow.AddHours(KEY_TTL_HOURS),
            LockedUntil = lockedUntil
        };
    }
}
