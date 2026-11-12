using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;

namespace PedeAqui.Application.UseCases.Users.Register;

public interface IRegisterUserUseCase
{
    Task<RegisterUserResponse> Execute(RegisterUserRequest request, CancellationToken cancellationToken);
}
