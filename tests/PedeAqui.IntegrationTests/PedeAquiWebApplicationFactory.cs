using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PedeAqui.Infrastructure.Database;

namespace PedeAqui.IntegrationTests;

public class PedeAquiWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ADMIN_EMAIL = "admin@pedeaqui.test";
    public const string ADMIN_PASSWORD = "Admin@123";

    private readonly string _databaseName = $"pedeaqui-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.UseSetting("ConnectionStrings:Connection", "Host=not-used-in-tests");
        builder.UseSetting("Settings:Jwt:SigningKey", "chave-de-assinatura-dos-testes-de-integracao-0123456789");
        builder.UseSetting("Settings:Jwt:ExpiresMinutes", "60");
        builder.UseSetting("Settings:Admin:Email", ADMIN_EMAIL);
        builder.UseSetting("Settings:Admin:Password", ADMIN_PASSWORD);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<PedeAquiDbContext>>();
            services.AddDbContext<PedeAquiDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
