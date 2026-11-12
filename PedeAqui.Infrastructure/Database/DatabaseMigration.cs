using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PedeAqui.Infrastructure.Database;

public static class DatabaseMigration
{
    public static async Task Migrate(IServiceProvider services)
    {
        var dbContext = services.GetRequiredService<PedeAquiDbContext>();
        await dbContext.Database.MigrateAsync();
    }
}
