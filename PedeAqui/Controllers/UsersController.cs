using Microsoft.AspNetCore.Mvc;
using PedeAqui.Application.UseCases.Users.Login;
using PedeAqui.Application.UseCases.Users.Register;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;

namespace PedeAqui.Controllers;

[Route("api/users")]
[ApiController]
public class UsersController : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(RegisterUserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterUserRequest request,
        [FromServices] IRegisterUserUseCase useCase)
    {
        var response = await useCase.Execute(request);
        return Created(string.Empty, response);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] ILoginUseCase useCase)
    {
        var response = await useCase.Execute(request);
        return Ok(response);
    }
}
