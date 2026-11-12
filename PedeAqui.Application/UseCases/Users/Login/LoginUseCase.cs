using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Domain.Repositories.Users;
using PedeAqui.Domain.Security;
using PedeAqui.Domain.Tokens;
using PedeAqui.Exception.ExceptionsBase;

namespace PedeAqui.Application.UseCases.Users.Login;

public class LoginUseCase : ILoginUseCase
{
    private readonly IUserReadOnlyRepository _readOnlyRepository;
    private readonly IPasswordEncripter _passwordEncripter;
    private readonly IAccessTokenGenerator _tokenGenerator;

    public LoginUseCase(
        IUserReadOnlyRepository readOnlyRepository,
        IPasswordEncripter passwordEncripter,
        IAccessTokenGenerator tokenGenerator)
    {
        _readOnlyRepository = readOnlyRepository;
        _passwordEncripter = passwordEncripter;
        _tokenGenerator = tokenGenerator;
    }

    public async Task<LoginResponse> Execute(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _readOnlyRepository.GetUserByEmail(request.Email, cancellationToken);

        if (user is null || !_passwordEncripter.Verify(request.Password, user.Password))
            throw new InvalidLoginException();

        return new LoginResponse
        {
            Name = user.Name,
            Token = _tokenGenerator.Generate(user)
        };
    }
}
