using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PedeAqui.Infrastructure.Database;

public static class DatabaseMigration
{
    public static async Task Migrate(IServiceProvider services, CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<PedeAquiDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
