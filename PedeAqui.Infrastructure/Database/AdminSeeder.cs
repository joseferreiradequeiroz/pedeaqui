using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Domain.Security;

namespace PedeAqui.Infrastructure.Database;

public static class AdminSeeder
{
    public static async Task Seed(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var email = configuration["Settings:Admin:Email"];
        var password = configuration["Settings:Admin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        var dbContext = services.GetRequiredService<PedeAquiDbContext>();

        if (await dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken))
            return;

        var passwordEncripter = services.GetRequiredService<IPasswordEncripter>();

        await dbContext.Users.AddAsync(new User
        {
            Name = configuration["Settings:Admin:Name"] ?? "Administrador",
            Email = email,
            Password = passwordEncripter.Encrypt(password),
            UserIdentifier = Guid.NewGuid(),
            Role = Roles.ADMIN
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
