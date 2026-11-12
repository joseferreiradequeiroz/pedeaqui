using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PedeAqui.Domain.Caching;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Idempotency;
using PedeAqui.Domain.Repositories.Inbox;
using PedeAqui.Domain.Repositories.Orders;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Domain.Repositories.Stock;
using PedeAqui.Domain.Messaging;
using PedeAqui.Domain.Payments;
using PedeAqui.Domain.Repositories.Users;
using PedeAqui.Domain.Security;
using PedeAqui.Domain.Services;
using PedeAqui.Domain.Tokens;
using PedeAqui.Infrastructure.Caching;
using PedeAqui.Infrastructure.Database;
using PedeAqui.Infrastructure.Database.Repositories;
using PedeAqui.Infrastructure.Messaging;
using PedeAqui.Infrastructure.Messaging.Outbox;
using PedeAqui.Infrastructure.Messaging.RabbitMq;
using PedeAqui.Infrastructure.Payments;
using PedeAqui.Infrastructure.Redis;
using PedeAqui.Infrastructure.Security.Cryptography;
using PedeAqui.Infrastructure.Security.Tokens;
using PedeAqui.Infrastructure.Services;

namespace PedeAqui.Infrastructure;

public static class DependencyInjectionExtension
{
    public static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        AddDbContext(services, configuration);
        AddRedis(services, configuration);
        AddRepositories(services);
        AddSecurity(services, configuration);
    }

    public static IServiceCollection AddPaymentGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PaymentGatewayOptions>(options => configuration.GetSection(PaymentGatewayOptions.SECTION).Bind(options));

        services.AddHttpClient<IPaymentGateway, HttpPaymentGateway>(PaymentGatewayResilience.CLIENT_NAME, (serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<PaymentGatewayOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
            })
            .AddPaymentGatewayResilience();

        return services;
    }

    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(options => configuration.GetSection(RabbitMqOptions.SECTION).Bind(options));
        services.Configure<OutboxOptions>(options => configuration.GetSection(OutboxOptions.SECTION).Bind(options));

        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddSingleton<RetryScheduler>();
        services.AddScoped<IOutboxProcessor, OutboxProcessor>();

        return services;
    }

    private static void AddDbContext(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Connection");

        services.Configure<DatabaseSettings>(settings => configuration.GetSection(DatabaseSettings.SECTION).Bind(settings));

        services.AddDbContext<PedeAquiDbContext>((serviceProvider, options) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<DatabaseSettings>>().Value;

            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(
                maxRetryCount: settings.MaxRetryCount,
                maxRetryDelay: TimeSpan.FromSeconds(settings.MaxRetryDelaySeconds),
                errorCodesToAdd: null));
        });
    }

    private static void AddRedis(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisOptions>(options => configuration.GetSection(RedisOptions.SECTION).Bind(options));
        services.AddRedisResilience();
        services.AddSingleton<RedisConnection>();
        services.AddSingleton<IRateLimiter, RedisFixedWindowRateLimiter>();
        services.AddScoped<IProductCacheInvalidator, ProductCacheInvalidator>();
        services.AddScoped<IIdempotencyLock, RedisIdempotencyLock>();
    }

    private static void AddRepositories(IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IUserReadOnlyRepository, UserRepository>();
        services.AddScoped<IUserWriteOnlyRepository, UserRepository>();

        services.AddScoped<ProductRepository>();
        services.AddScoped<IProductReadOnlyRepository>(serviceProvider => new CachedProductReadOnlyRepository(
            serviceProvider.GetRequiredService<ProductRepository>(),
            serviceProvider.GetRequiredService<RedisConnection>(),
            serviceProvider.GetRequiredService<IOptions<RedisOptions>>()));
        services.AddScoped<IProductWriteOnlyRepository>(serviceProvider => serviceProvider.GetRequiredService<ProductRepository>());
        services.AddScoped<IProductUpdateOnlyRepository>(serviceProvider => serviceProvider.GetRequiredService<ProductRepository>());

        services.AddScoped<IStockMovementRepository, StockMovementRepository>();

        services.AddScoped<IOrderReadOnlyRepository, OrderRepository>();
        services.AddScoped<IOrderWriteOnlyRepository, OrderRepository>();
        services.AddScoped<IOrderUpdateOnlyRepository, OrderRepository>();

        services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        services.AddScoped<IInboxRepository, InboxRepository>();

        services.AddScoped<IOutbox, DatabaseOutbox>();

        services.AddScoped<ILoggedUser, LoggedUser>();
    }

    private static void AddSecurity(IServiceCollection services, IConfiguration configuration)
    {
        var expirationTime = configuration.GetValue<uint>("Settings:Jwt:ExpiresMinutes");
        var signingKey = configuration.GetValue<string>("Settings:Jwt:SigningKey")!;

        services.AddScoped<IAccessTokenGenerator>(_ => new JwtTokenGenerator(expirationTime, signingKey));
        services.AddScoped<IPasswordEncripter, BCryptPasswordEncripter>();
    }
}
