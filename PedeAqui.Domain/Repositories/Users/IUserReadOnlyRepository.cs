using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Users;

public interface IUserReadOnlyRepository
{
    Task<bool> ExistsUserWithEmail(string email, CancellationToken cancellationToken);
    Task<User?> GetUserByEmail(string email, CancellationToken cancellationToken);
}
