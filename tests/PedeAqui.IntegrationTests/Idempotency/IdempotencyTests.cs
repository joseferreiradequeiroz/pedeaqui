using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PedeAqui.Application.Idempotency;
using PedeAqui.Application.UseCases.Orders.Create;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Exception;
using PedeAqui.IntegrationTests.Support;
using RequestBuilder = PedeAqui.IntegrationTests.Support.RequestBuilder;

namespace PedeAqui.IntegrationTests.Idempotency;

[Collection(IntegrationCollection.Name)]
public class IdempotencyTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private const string ORDERS_PATH = "/api/orders";
    private const string UNREACHABLE_REDIS = "localhost:1," + PostgresContainerFixture.REDIS_TIMEOUT_SETTINGS;
    private const int CONCURRENT_REQUESTS = 10;
    private static readonly TimeSpan MaximumWaitForCompletion = TimeSpan.FromSeconds(10);

    private readonly PedeAquiWebApplicationFactory _factory;
    private readonly PostgresContainerFixture _infrastructure;

    public IdempotencyTests(PedeAquiWebApplicationFactory factory, PostgresContainerFixture infrastructure)
    {
        _factory = factory;
        _infrastructure = infrastructure;
    }

    [Fact]
    public async Task Same_key_same_body_sequential_creates_one_order_and_replays()
    {
        var product = await ProductWithStock(_factory, stock: 10);
        var (customer, userId) = await Customer(_factory);
        var key = PedeAquiApiClient.NewIdempotencyKey();
        var order = RequestBuilder.Order((product.Id, 2));

        var first = await customer.CreateOrder(order, key);
        var second = await customer.CreateOrder(order, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.False(PedeAquiApiClient.WasReplayed(first));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.True(PedeAquiApiClient.WasReplayed(second));
        var created = await PedeAquiApiClient.Read<OrderResponse>(first);
        var replayed = await PedeAquiApiClient.Read<OrderResponse>(second);
        Assert.Equal(created.Id, replayed.Id);
        Assert.Equal(created.Total, replayed.Total);
        Assert.Equal(1, await _factory.OrderCountOf(userId));
        Assert.Equal(8, await (await Admin(_factory)).StockOf(product.Id));
    }

    [Fact]
    public async Task Same_key_concurrent_requests_create_one_order_and_others_get_409_or_replay()
    {
        var product = await ProductWithStock(_factory, stock: 50);
        var (customer, userId) = await Customer(_factory);
        var key = PedeAquiApiClient.NewIdempotencyKey();
        var order = RequestBuilder.Order((product.Id, 1));

        var responses = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_REQUESTS).Select(_ => customer.CreateOrder(order, key)));

        Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created && !PedeAquiApiClient.WasReplayed(response));
        Assert.Equal(1, await _factory.OrderCountOf(userId));
        Assert.Equal(49, await (await Admin(_factory)).StockOf(product.Id));
    }

    [Fact]
    public async Task Same_key_different_body_returns_422()
    {
        var product = await ProductWithStock(_factory, stock: 10);
        var (customer, userId) = await Customer(_factory);
        var key = PedeAquiApiClient.NewIdempotencyKey();
        (await customer.CreateOrder(RequestBuilder.Order((product.Id, 1)), key)).EnsureSuccessStatusCode();

        var response = await customer.CreateOrder(RequestBuilder.Order((product.Id, 3)), key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(ResourceErrorMessages.IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST, await PedeAquiApiClient.ReadErrors(response));
        Assert.Equal(1, await _factory.OrderCountOf(userId));
    }

    [Fact]
    public async Task Missing_key_returns_400()
    {
        var product = await ProductWithStock(_factory, stock: 10);
        var (customer, userId) = await Customer(_factory);

        var response = await customer.CreateOrderWithoutIdempotencyKey(RequestBuilder.Order((product.Id, 1)));

        await AssertInvalidKey(response);
        Assert.Equal(0, await _factory.OrderCountOf(userId));
    }

    [Fact]
    public async Task Key_longer_than_100_chars_returns_400()
    {
        var product = await ProductWithStock(_factory, stock: 10);
        var (customer, userId) = await Customer(_factory);

        var response = await customer.CreateOrder(RequestBuilder.Order((product.Id, 1)), new string('k', IdempotencyRequest.KEY_MAX_LENGTH + 1));

        await AssertInvalidKey(response);
        Assert.Equal(0, await _factory.OrderCountOf(userId));
        var keyWithMaximumLength = await customer.CreateOrder(RequestBuilder.Order((product.Id, 1)), new string('k', IdempotencyRequest.KEY_MAX_LENGTH));
        Assert.Equal(HttpStatusCode.Created, keyWithMaximumLength.StatusCode);
    }

    [Fact]
    public async Task Whitespace_key_returns_400()
    {
        var product = await ProductWithStock(_factory, stock: 10);
        var (customer, userId) = await Customer(_factory);

        var response = await customer.CreateOrder(RequestBuilder.Order((product.Id, 1)), "   ");

        await AssertInvalidKey(response);
        Assert.Equal(0, await _factory.OrderCountOf(userId));
    }

    [Fact]
    public async Task Business_422_is_stored_and_replayed()
    {
        var product = await ProductWithStock(_factory, stock: 1);
        var (customer, userId) = await Customer(_factory);
        var key = PedeAquiApiClient.NewIdempotencyKey();
        var order = RequestBuilder.Order((product.Id, 5));
        var first = await customer.CreateOrder(order, key);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, first.StatusCode);
        Assert.Equal(IdempotencyStatus.Completed, (await _factory.IdempotencyRequestOf(userId, key))!.Status);
        (await (await Admin(_factory)).StockEntry(product.Id, 10)).EnsureSuccessStatusCode();

        var replayed = await customer.CreateOrder(order, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, replayed.StatusCode);
        Assert.True(PedeAquiApiClient.WasReplayed(replayed));
        Assert.Equal("application/problem+json", replayed.Content.Headers.ContentType?.MediaType);
        var firstProblem = await ReadJson(first);
        var replayedProblem = await ReadJson(replayed);
        Assert.StartsWith(ResourceErrorMessages.INSUFFICIENT_STOCK, replayedProblem.GetProperty("detail").GetString());
        Assert.Equal(firstProblem.GetProperty("detail").GetString(), replayedProblem.GetProperty("detail").GetString());
        Assert.Equal(firstProblem.GetProperty("traceId").GetString(), replayedProblem.GetProperty("traceId").GetString());
        Assert.Equal(firstProblem.GetProperty("correlationId").GetString(), replayedProblem.GetProperty("correlationId").GetString());
        Assert.Equal(firstProblem.GetProperty("status").GetInt32(), replayedProblem.GetProperty("status").GetInt32());
        Assert.Equal(firstProblem.GetProperty("title").GetString(), replayedProblem.GetProperty("title").GetString());
        Assert.Equal(firstProblem.GetProperty("errors").GetRawText(), replayedProblem.GetProperty("errors").GetRawText());
        Assert.Equal(0, await _factory.OrderCountOf(userId));
    }

    [Fact]
    public async Task Same_key_different_users_are_independent()
    {
        var product = await ProductWithStock(_factory, stock: 10);
        var (firstCustomer, _) = await Customer(_factory);
        var (secondCustomer, _) = await Customer(_factory);
        var key = PedeAquiApiClient.NewIdempotencyKey();
        var order = RequestBuilder.Order((product.Id, 1));

        var first = await firstCustomer.CreateOrder(order, key);
        var second = await secondCustomer.CreateOrder(order, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(PedeAquiApiClient.WasReplayed(second));
        Assert.NotEqual(
            (await PedeAquiApiClient.Read<OrderResponse>(first)).Id,
            (await PedeAquiApiClient.Read<OrderResponse>(second)).Id);
    }

    [Fact]
    public async Task Works_when_redis_is_down()
    {
        using var factory = new PedeAquiWebApplicationFactory(_infrastructure) { RedisConnectionString = UNREACHABLE_REDIS };
        var product = await ProductWithStock(factory, stock: 50);
        var (customer, userId) = await Customer(factory);
        var order = RequestBuilder.Order((product.Id, 1));
        var sequentialKey = PedeAquiApiClient.NewIdempotencyKey();
        var concurrentKey = PedeAquiApiClient.NewIdempotencyKey();

        var first = await customer.CreateOrder(order, sequentialKey);
        var second = await customer.CreateOrder(order, sequentialKey);
        var concurrent = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_REQUESTS).Select(_ => customer.CreateOrder(order, concurrentKey)));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.True(PedeAquiApiClient.WasReplayed(second));
        Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.Created && !PedeAquiApiClient.WasReplayed(response));
        Assert.All(concurrent, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
        Assert.Equal(2, await factory.OrderCountOf(userId));
    }

    [Fact]
    public async Task Stuck_in_progress_key_is_taken_over_after_lock_expires()
    {
        var product = await ProductWithStock(_factory, stock: 10);
        var (customer, userId) = await Customer(_factory);
        var key = PedeAquiApiClient.NewIdempotencyKey();
        var order = RequestBuilder.Order((product.Id, 1));
        await _factory.AddIdempotencyRequest(StuckInProgress(userId, key, order, lockedUntil: DateTime.UtcNow.AddMinutes(5)));

        var whileLocked = await customer.CreateOrder(order, key);

        Assert.Equal(HttpStatusCode.Conflict, whileLocked.StatusCode);
        Assert.Contains(ResourceErrorMessages.IDEMPOTENCY_REQUEST_IN_PROGRESS, await PedeAquiApiClient.ReadErrors(whileLocked));
        await _factory.ChangeIdempotencyRequest(userId, key, stuck => stuck.LockedUntil = DateTime.UtcNow.AddMinutes(-1));

        var afterLockExpired = await customer.CreateOrder(order, key);

        Assert.Equal(HttpStatusCode.Created, afterLockExpired.StatusCode);
        Assert.False(PedeAquiApiClient.WasReplayed(afterLockExpired));
        Assert.Equal(1, await _factory.OrderCountOf(userId));
        var stored = await _factory.IdempotencyRequestOf(userId, key);
        Assert.Equal(IdempotencyStatus.Completed, stored!.Status);
        Assert.Equal(201, stored.ResponseStatusCode);
    }

    [Fact]
    public async Task Request_cancelled_by_client_after_commit_still_completes_key()
    {
        var probe = new ClientDisconnectProbe();
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(probe);
            services.AddScoped<CreateOrderUseCase>();
            services.AddScoped<ICreateOrderUseCase, CommitThenWaitForClientDisconnectUseCase>();
        }));
        var product = await ProductWithStock(_factory, stock: 10);
        var customer = new PedeAquiApiClient(factory.CreateClient());
        var user = await customer.AuthenticateAsNewCustomer();
        var userId = await _factory.UserIdByEmail(user.Email);
        var key = PedeAquiApiClient.NewIdempotencyKey();
        var order = RequestBuilder.Order((product.Id, 1));
        using var clientCancellation = new CancellationTokenSource();

        var abandonedRequest = customer.CreateOrder(order, key, clientCancellation.Token);
        await probe.OrderCommitted.Task.WaitAsync(MaximumWaitForCompletion);
        clientCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandonedRequest);
        var stored = await WaitUntilCompleted(userId, key);
        Assert.True(probe.ServerSawClientDisconnect);
        Assert.Equal(201, stored.ResponseStatusCode);
        var retry = await customer.CreateOrder(order, key);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.True(PedeAquiApiClient.WasReplayed(retry));
        Assert.Equal(1, await _factory.OrderCountOf(userId));
    }

    [Fact]
    public async Task Server_error_releases_the_key_so_a_retry_executes()
    {
        var probe = new FirstCallFailureProbe();
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(probe);
            services.AddScoped<CreateOrderUseCase>();
            services.AddScoped<ICreateOrderUseCase, FailFirstCallCreateOrderUseCase>();
        }));
        var product = await ProductWithStock(_factory, stock: 10);
        var customer = new PedeAquiApiClient(factory.CreateClient());
        var user = await customer.AuthenticateAsNewCustomer();
        var userId = await _factory.UserIdByEmail(user.Email);
        var key = PedeAquiApiClient.NewIdempotencyKey();
        var order = RequestBuilder.Order((product.Id, 1));

        var failed = await customer.CreateOrder(order, key);

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Null(await _factory.IdempotencyRequestOf(userId, key));
        Assert.Equal(0, await _factory.OrderCountOf(userId));

        var retry = await customer.CreateOrder(order, key);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.False(PedeAquiApiClient.WasReplayed(retry));
        Assert.Equal(2, probe.Calls);
        Assert.Equal(1, await _factory.OrderCountOf(userId));
    }

    [Fact]
    public async Task Conflict_409_from_the_action_releases_the_key_so_a_retry_executes()
    {
        var probe = new FirstCallFailureProbe();
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(probe);
            services.AddScoped<CreateOrderUseCase>();
            services.AddScoped<ICreateOrderUseCase, ConflictFirstCallCreateOrderUseCase>();
        }));
        var product = await ProductWithStock(_factory, stock: 10);
        var customer = new PedeAquiApiClient(factory.CreateClient());
        var user = await customer.AuthenticateAsNewCustomer();
        var userId = await _factory.UserIdByEmail(user.Email);
        var key = PedeAquiApiClient.NewIdempotencyKey();
        var order = RequestBuilder.Order((product.Id, 1));

        var conflicted = await customer.CreateOrder(order, key);

        Assert.Equal(HttpStatusCode.Conflict, conflicted.StatusCode);
        Assert.Contains(ResourceErrorMessages.CONCURRENCY_CONFLICT, await PedeAquiApiClient.ReadErrors(conflicted));
        Assert.Null(await _factory.IdempotencyRequestOf(userId, key));

        var retry = await customer.CreateOrder(order, key);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.False(PedeAquiApiClient.WasReplayed(retry));
        Assert.Equal(2, probe.Calls);
        Assert.Equal(1, await _factory.OrderCountOf(userId));
    }

    [Fact]
    public async Task Body_with_duplicated_property_names_returns_400_and_stores_nothing()
    {
        var product = await ProductWithStock(_factory, stock: 10);
        var (customer, userId) = await Customer(_factory);
        var key = PedeAquiApiClient.NewIdempotencyKey();
        var singleBody = JsonSerializer.Serialize(RequestBuilder.Order((product.Id, 1)), PedeAquiApiClient.JsonOptions);
        var properties = singleBody[1..^1];
        var bodyWithDuplicatedProperties = "{" + properties + "," + properties + "}";

        var response = await customer.CreateOrderWithRawBody(bodyWithDuplicatedProperties, key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ResourceErrorMessages.IDEMPOTENCY_REQUEST_BODY_INVALID, await PedeAquiApiClient.ReadErrors(response));
        Assert.Null(await _factory.IdempotencyRequestOf(userId, key));
        Assert.Equal(0, await _factory.OrderCountOf(userId));
    }

    [Fact]
    public async Task Expired_completed_key_is_treated_as_absent_and_the_request_executes()
    {
        var product = await ProductWithStock(_factory, stock: 10);
        var (customer, userId) = await Customer(_factory);
        var key = PedeAquiApiClient.NewIdempotencyKey();
        var order = RequestBuilder.Order((product.Id, 1));
        var expired = StuckInProgress(userId, key, order, lockedUntil: DateTime.UtcNow.AddMinutes(-1));
        expired.Status = IdempotencyStatus.Completed;
        expired.ResponseStatusCode = (int)HttpStatusCode.Created;
        expired.ResponseBody = """{"id":999999}""";
        expired.CompletedAt = DateTime.UtcNow.AddHours(-25);
        expired.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _factory.AddIdempotencyRequest(expired);

        var response = await customer.CreateOrder(order, key);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(PedeAquiApiClient.WasReplayed(response));
        Assert.Equal(1, await _factory.OrderCountOf(userId));
        var stored = await _factory.IdempotencyRequestOf(userId, key);
        Assert.NotEqual(expired.Id, stored!.Id);
        Assert.Equal(IdempotencyStatus.Completed, stored.Status);
        Assert.True(stored.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Stock_entry_with_same_key_applies_once()
    {
        var admin = await Admin(_factory);
        var product = await admin.CreateProductWithStock(stock: 0);
        var key = PedeAquiApiClient.NewIdempotencyKey();

        var first = await admin.StockEntry(product.Id, 5, key);
        var second = await admin.StockEntry(product.Id, 5, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.True(PedeAquiApiClient.WasReplayed(second));
        Assert.Equal(
            (await PedeAquiApiClient.Read<StockMovementResponse>(first)).Id,
            (await PedeAquiApiClient.Read<StockMovementResponse>(second)).Id);
        Assert.Equal(5, await admin.StockOf(product.Id));
    }

    private async Task<IdempotencyRequest> WaitUntilCompleted(long userId, string key)
    {
        var deadline = DateTime.UtcNow.Add(MaximumWaitForCompletion);

        while (DateTime.UtcNow < deadline)
        {
            var stored = await _factory.IdempotencyRequestOf(userId, key);
            if (stored?.Status == IdempotencyStatus.Completed)
                return stored;

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException("A chave de idempotência não foi concluída depois que o cliente desistiu da requisição.");
    }

    private static IdempotencyRequest StuckInProgress(long userId, string key, OrderRequest order, DateTime lockedUntil)
    {
        var body = JsonSerializer.Serialize(order, PedeAquiApiClient.JsonOptions);

        return new IdempotencyRequest
        {
            UserId = userId,
            Key = key,
            Method = HttpMethod.Post.Method,
            Path = ORDERS_PATH,
            RequestBody = body,
            RequestHash = IdempotencyRequestHash.Of(body),
            Status = IdempotencyStatus.InProgress,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            LockedUntil = lockedUntil
        };
    }

    private static async Task AssertInvalidKey(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ResourceErrorMessages.IDEMPOTENCY_KEY_INVALID, await PedeAquiApiClient.ReadErrors(response));
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<ProductResponse> ProductWithStock(PedeAquiWebApplicationFactory factory, int stock)
    {
        return await (await Admin(factory)).CreateProductWithStock(stock);
    }

    private static async Task<(PedeAquiApiClient Client, long UserId)> Customer(PedeAquiWebApplicationFactory factory)
    {
        var customer = new PedeAquiApiClient(factory.CreateClient());
        var user = await customer.AuthenticateAsNewCustomer();
        return (customer, await factory.UserIdByEmail(user.Email));
    }

    private static async Task<PedeAquiApiClient> Admin(PedeAquiWebApplicationFactory factory)
    {
        var admin = new PedeAquiApiClient(factory.CreateClient());
        await admin.AuthenticateAsAdmin();
        return admin;
    }
}
