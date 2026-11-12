using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories.Users;

namespace PedeAqui.UnitTests.Fakes;

public sealed class InMemoryUserRepository : IUserReadOnlyRepository, IUserWriteOnlyRepository
{
    private readonly List<User> _users = [];

    public InMemoryUserRepository(params User[] users)
    {
        _users.AddRange(users);
    }

    public IReadOnlyList<User> Users => _users;

    public Task Add(User user, CancellationToken cancellationToken)
    {
        _users.Add(user);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsUserWithEmail(string email, CancellationToken cancellationToken) => Task.FromResult(_users.Any(user => user.Email == email));

    public Task<User?> GetUserByEmail(string email, CancellationToken cancellationToken) => Task.FromResult(_users.FirstOrDefault(user => user.Email == email));
}
