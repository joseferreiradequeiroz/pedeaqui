using Microsoft.EntityFrameworkCore;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories.Users;

namespace PedeAqui.Infrastructure.Database.Repositories;

internal class UserRepository : IUserReadOnlyRepository, IUserWriteOnlyRepository
{
    private readonly PedeAquiDbContext _dbContext;

    public UserRepository(PedeAquiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Add(User user, CancellationToken cancellationToken)
    {
        await _dbContext.Users.AddAsync(user, cancellationToken);
    }

    public async Task<bool> ExistsUserWithEmail(string email, CancellationToken cancellationToken)
    {
        return await _dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken);
    }

    public async Task<User?> GetUserByEmail(string email, CancellationToken cancellationToken)
    {
        return await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
    }
}
