using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PedeAqui.Infrastructure.Database;

public static class DatabaseConnectivity
{
    public static async Task<bool> CanConnect(IServiceProvider services, CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<PedeAquiDbContext>();
        return await dbContext.Database.CanConnectAsync(cancellationToken);
    }
}
