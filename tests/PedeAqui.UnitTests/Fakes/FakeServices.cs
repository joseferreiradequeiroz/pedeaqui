using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Security;
using PedeAqui.Domain.Services;
using PedeAqui.Domain.Tokens;

namespace PedeAqui.UnitTests.Fakes;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int CommitCount { get; private set; }

    public Task Commit()
    {
        CommitCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeLoggedUser : ILoggedUser
{
    private readonly User _user;

    public FakeLoggedUser(User user)
    {
        _user = user;
    }

    public Task<User> Get() => Task.FromResult(_user);
}

public sealed class FakePasswordEncripter : IPasswordEncripter
{
    private const string PREFIX = "hashed:";

    public string Encrypt(string password) => PREFIX + password;

    public bool Verify(string password, string passwordHash) => passwordHash == PREFIX + password;
}

public sealed class FakeAccessTokenGenerator : IAccessTokenGenerator
{
    public string Generate(User user) => $"token-{user.UserIdentifier}";
}
