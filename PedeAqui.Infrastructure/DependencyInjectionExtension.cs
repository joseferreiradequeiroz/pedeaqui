using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Repositories.Orders;
using PedeAqui.Domain.Repositories.Products;
using PedeAqui.Domain.Repositories.Stock;
using PedeAqui.Domain.Repositories.Users;
using PedeAqui.Domain.Security;
using PedeAqui.Domain.Services;
using PedeAqui.Domain.Tokens;
using PedeAqui.Infrastructure.Database;
using PedeAqui.Infrastructure.Database.Repositories;
using PedeAqui.Infrastructure.Security.Cryptography;
using PedeAqui.Infrastructure.Security.Tokens;
using PedeAqui.Infrastructure.Services;

namespace PedeAqui.Infrastructure;

public static class DependencyInjectionExtension
{
    public static void AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        AddDbContext(services, configuration);
        AddRepositories(services);
        AddSecurity(services, configuration);
    }

    private static void AddDbContext(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Connection");

        services.AddDbContext<PedeAquiDbContext>(options => options.UseNpgsql(connectionString));
    }

    private static void AddRepositories(IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IUserReadOnlyRepository, UserRepository>();
        services.AddScoped<IUserWriteOnlyRepository, UserRepository>();

        services.AddScoped<IProductReadOnlyRepository, ProductRepository>();
        services.AddScoped<IProductWriteOnlyRepository, ProductRepository>();
        services.AddScoped<IProductUpdateOnlyRepository, ProductRepository>();

        services.AddScoped<IStockMovementRepository, StockMovementRepository>();

        services.AddScoped<IOrderReadOnlyRepository, OrderRepository>();
        services.AddScoped<IOrderWriteOnlyRepository, OrderRepository>();
        services.AddScoped<IOrderUpdateOnlyRepository, OrderRepository>();

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
