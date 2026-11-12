using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;

namespace PedeAqui.Application.UseCases.Users.Login;

public interface ILoginUseCase
{
    Task<LoginResponse> Execute(LoginRequest request, CancellationToken cancellationToken);
}
