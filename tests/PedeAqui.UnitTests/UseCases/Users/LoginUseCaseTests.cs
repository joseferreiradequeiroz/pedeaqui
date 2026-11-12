using PedeAqui.Application.UseCases.Users.Login;
using PedeAqui.Communication.Requests;
using PedeAqui.Exception;
using PedeAqui.Exception.ExceptionsBase;
using PedeAqui.UnitTests.Builders;
using PedeAqui.UnitTests.Fakes;

namespace PedeAqui.UnitTests.UseCases.Users;

public class LoginUseCaseTests
{
    private const string PASSWORD = "Senha@123";

    [Fact]
    public async Task Execute_CorrectCredentials_ReturnsToken()
    {
        var user = UserBuilder.Build();
        user.Password = new FakePasswordEncripter().Encrypt(PASSWORD);

        var response = await CreateUseCase(user).Execute(new LoginRequest { Email = user.Email, Password = PASSWORD });

        Assert.Equal(user.Name, response.Name);
        Assert.Equal($"token-{user.UserIdentifier}", response.Token);
    }

    [Fact]
    public async Task Execute_WrongPassword_ThrowsInvalidLogin()
    {
        var user = UserBuilder.Build();
        user.Password = new FakePasswordEncripter().Encrypt(PASSWORD);

        var exception = await Assert.ThrowsAsync<InvalidLoginException>(
            () => CreateUseCase(user).Execute(new LoginRequest { Email = user.Email, Password = "Outra@123" }));

        Assert.Equal(ResourceErrorMessages.EMAIL_OR_PASSWORD_INVALID, exception.Message);
    }

    [Fact]
    public async Task Execute_UnknownEmail_ThrowsSameInvalidLogin()
    {
        await Assert.ThrowsAsync<InvalidLoginException>(
            () => CreateUseCase().Execute(new LoginRequest { Email = "ninguem@pedeaqui.dev", Password = PASSWORD }));
    }

    private static LoginUseCase CreateUseCase(params PedeAqui.Domain.Entities.User[] users)
    {
        return new LoginUseCase(new InMemoryUserRepository(users), new FakePasswordEncripter(), new FakeAccessTokenGenerator());
    }
}
