using PedeAqui.Application.UseCases.Users.Register;
using PedeAqui.Domain.Enums;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Fakes;
using PedeAqui.UnitTests.Support;

namespace PedeAqui.UnitTests.UseCases.Users;

public class RegisterUserUseCaseTests
{
    private readonly InMemoryUserRepository _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Execute_ValidRequest_StoresCustomerWithHashedPassword()
    {
        var request = RequestBuilder.RegisterUser();

        await CreateUseCase().Execute(request);

        var stored = Assert.Single(_repository.Users);
        Assert.Equal(request.Email, stored.Email);
        Assert.Equal(Roles.CUSTOMER, stored.Role);
        Assert.Equal($"hashed:{request.Password}", stored.Password);
        Assert.NotEqual(Guid.Empty, stored.UserIdentifier);
        Assert.Equal(1, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Execute_ValidRequest_ReturnsNameAndToken()
    {
        var request = RequestBuilder.RegisterUser();

        var response = await CreateUseCase().Execute(request);

        Assert.Equal(request.Name, response.Name);
        Assert.Equal($"token-{_repository.Users[0].UserIdentifier}", response.Token);
    }

    [Fact]
    public async Task Execute_EmailAlreadyRegistered_ThrowsAndDoesNotCommit()
    {
        var existing = UserBuilder.Build();
        await _repository.Add(existing);
        var request = RequestBuilder.RegisterUser();
        request.Email = existing.Email;

        var exception = await Assert.ThrowsAsync<ErrorOnValidationException>(() => CreateUseCase().Execute(request));

        Assert.Contains(ResourceErrorMessages.EMAIL_ALREADY_REGISTERED, exception.Errors);
        Assert.Single(_repository.Users);
        Assert.Equal(0, _unitOfWork.CommitCount);
    }

    [Fact]
    public async Task Execute_InvalidRequest_ReturnsAllErrors()
    {
        var request = RequestBuilder.RegisterUser();
        request.Name = string.Empty;
        request.Password = "fraca";

        var exception = await Assert.ThrowsAsync<ErrorOnValidationException>(() => CreateUseCase().Execute(request));

        Assert.Equal(2, exception.Errors.Count);
        Assert.Empty(_repository.Users);
    }

    private RegisterUserUseCase CreateUseCase()
    {
        return new RegisterUserUseCase(
            MapperFactory.Create(), _repository, _repository, new FakePasswordEncripter(), new FakeAccessTokenGenerator(), _unitOfWork);
    }
}
