using PedeAqui.Domain.Caching;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Repositories;
using PedeAqui.Domain.Security;
using PedeAqui.Domain.Services;
using PedeAqui.Domain.Tokens;

namespace PedeAqui.UnitTests.Fakes;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int CommitCount { get; private set; }
    public int SaveCount { get; private set; }

    public Task SaveChanges(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task Commit(CancellationToken cancellationToken)
    {
        CommitCount++;
        return Task.CompletedTask;
    }

    public async Task<T> ExecuteInTransaction<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var result = await operation(cancellationToken);
        CommitCount++;
        return result;
    }

    public async Task ExecuteInTransaction(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await operation(cancellationToken);
        CommitCount++;
    }
}

public sealed class FakeProductCacheInvalidator : IProductCacheInvalidator
{
    private readonly FakeUnitOfWork _unitOfWork;

    public FakeProductCacheInvalidator(FakeUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public List<long> InvalidatedProductIds { get; } = [];
    public int InvalidationCount { get; private set; }
    public int CommitCountAtLastInvalidation { get; private set; }

    public Task Invalidate(IEnumerable<long> productIds, CancellationToken cancellationToken)
    {
        InvalidatedProductIds.AddRange(productIds);
        InvalidationCount++;
        CommitCountAtLastInvalidation = _unitOfWork.CommitCount;
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

    public Task<User> Get(CancellationToken cancellationToken) => Task.FromResult(_user);
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
