using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ConnectionMultiplexer = StackExchange.Redis.ConnectionMultiplexer;
using PedeAqui.Domain.Entities;
using PedeAqui.Infrastructure.Database;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests;

public class PedeAquiWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ADMIN_EMAIL = "admin@pedeaqui.test";
    public const string ADMIN_PASSWORD = "Admin@123";
    private const int DEFAULT_RATE_LIMIT = 1_000_000;

    private readonly string _connectionString;
    private readonly RabbitMqEndpoint _rabbitMq;
    private readonly string _instanceName = $"test-{Guid.NewGuid():N}:";
    private readonly Lazy<ConnectionMultiplexer> _redisInspector;
    private string _redisConnectionString;

    public PedeAquiWebApplicationFactory(PostgresContainerFixture postgres) : this(postgres, postgres.CreateDatabaseConnectionString())
    {
    }

    internal PedeAquiWebApplicationFactory(PostgresContainerFixture postgres, string databaseConnectionString)
    {
        _connectionString = databaseConnectionString;
        _rabbitMq = postgres.RabbitMq;
        _redisConnectionString = postgres.RedisConnectionString;
        _redisInspector = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(RedisConnectionString));
    }

    public string DatabaseConnectionString => _connectionString;

    public string RedisInstanceName => _instanceName;

    public string RedisConnectionString
    {
        get => _redisConnectionString;
        init => _redisConnectionString = value;
    }

    public async Task<bool> CacheKeyExists(string key)
    {
        return await _redisInspector.Value.GetDatabase().KeyExistsAsync($"{_instanceName}{key}");
    }

    public async Task ChangeProduct(long productId, Action<Product> change)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        var product = await dbContext.Set<Product>().SingleAsync(product => product.Id == productId);
        change(product);
        await dbContext.SaveChangesAsync();
    }

    public async Task ChangeOrder(long orderId, Action<Order> change)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        var order = await dbContext.Set<Order>().SingleAsync(order => order.Id == orderId);
        change(order);
        await dbContext.SaveChangesAsync();
    }

    public async Task<long> UserIdByEmail(string email)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        return await dbContext.Set<User>().Where(user => user.Email == email).Select(user => user.Id).SingleAsync();
    }

    public async Task<int> OrderCountOf(long userId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        return await dbContext.Set<Order>().CountAsync(order => order.UserId == userId);
    }

    public async Task<List<OutboxMessage>> OutboxMessages()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        return await dbContext.Set<OutboxMessage>().AsNoTracking().ToListAsync();
    }

    public async Task<IdempotencyRequest?> IdempotencyRequestOf(long userId, string key)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        return await dbContext.Set<IdempotencyRequest>().AsNoTracking().SingleOrDefaultAsync(request => request.UserId == userId && request.Key == key);
    }

    public async Task AddIdempotencyRequest(IdempotencyRequest request)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        dbContext.Add(request);
        await dbContext.SaveChangesAsync();
    }

    public async Task ChangeIdempotencyRequest(long userId, string key, Action<IdempotencyRequest> change)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PedeAquiDbContext>();
        var request = await dbContext.Set<IdempotencyRequest>().SingleAsync(request => request.UserId == userId && request.Key == key);
        change(request);
        await dbContext.SaveChangesAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.UseSetting("ConnectionStrings:Connection", _connectionString);
        builder.UseSetting("ConnectionStrings:Redis", RedisConnectionString);
        builder.UseSetting("Redis:InstanceName", _instanceName);
        builder.UseSetting("RabbitMq:HostName", _rabbitMq.HostName);
        builder.UseSetting("RabbitMq:Port", _rabbitMq.Port.ToString());
        builder.UseSetting("RabbitMq:UserName", _rabbitMq.UserName);
        builder.UseSetting("RabbitMq:Password", _rabbitMq.Password);
        builder.UseSetting("Settings:Jwt:SigningKey", "chave-de-assinatura-dos-testes-de-integracao-0123456789");
        builder.UseSetting("Settings:Jwt:ExpiresMinutes", "60");
        builder.UseSetting("Settings:Admin:Email", ADMIN_EMAIL);
        builder.UseSetting("Settings:Admin:Password", ADMIN_PASSWORD);
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("RateLimit:Login:Limit", DEFAULT_RATE_LIMIT.ToString());
        builder.UseSetting("RateLimit:Authenticated:Limit", DEFAULT_RATE_LIMIT.ToString());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _redisInspector.IsValueCreated)
            _redisInspector.Value.Dispose();

        base.Dispose(disposing);

        if (disposing)
            NpgsqlConnection.ClearAllPools();
    }
}
