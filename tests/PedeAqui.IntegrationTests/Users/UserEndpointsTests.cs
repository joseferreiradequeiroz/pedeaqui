using System.Net;
using PedeAqui.Communication.Requests;
using PedeAqui.Communication.Responses;
using PedeAqui.Exception;
using PedeAqui.IntegrationTests.Support;

namespace PedeAqui.IntegrationTests.Users;

public class UserEndpointsTests : IClassFixture<PedeAquiWebApplicationFactory>
{
    private readonly PedeAquiWebApplicationFactory _factory;

    public UserEndpointsTests(PedeAquiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_ValidUser_ReturnsCreatedWithToken()
    {
        var api = new PedeAquiApiClient(_factory.CreateClient());
        var request = RequestBuilder.RegisterUser();

        var response = await api.RegisterUser(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await PedeAquiApiClient.Read<RegisterUserResponse>(response);
        Assert.Equal(request.Name, body.Name);
        Assert.False(string.IsNullOrWhiteSpace(body.Token));
    }

    [Fact]
    public async Task Register_DuplicatedEmail_ReturnsBadRequest()
    {
        var api = new PedeAquiApiClient(_factory.CreateClient());
        var request = RequestBuilder.RegisterUser();
        await api.RegisterUser(request);

        var response = await api.RegisterUser(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ResourceErrorMessages.EMAIL_ALREADY_REGISTERED, await PedeAquiApiClient.ReadErrors(response));
    }

    [Fact]
    public async Task Register_WeakPassword_ReturnsBadRequestWithReason()
    {
        var api = new PedeAquiApiClient(_factory.CreateClient());
        var request = RequestBuilder.RegisterUser();
        request.Password = "fraca";

        var response = await api.RegisterUser(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ResourceErrorMessages.PASSWORD_MIN_LENGTH, await PedeAquiApiClient.ReadErrors(response));
    }

    [Fact]
    public async Task Login_CorrectCredentials_ReturnsToken()
    {
        var api = new PedeAquiApiClient(_factory.CreateClient());
        var user = RequestBuilder.RegisterUser();
        await api.RegisterUser(user);

        var response = await api.Login(new LoginRequest { Email = user.Email, Password = user.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await PedeAquiApiClient.Read<LoginResponse>(response)).Token));
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        var api = new PedeAquiApiClient(_factory.CreateClient());
        var user = RequestBuilder.RegisterUser();
        await api.RegisterUser(user);

        var response = await api.Login(new LoginRequest { Email = user.Email, Password = "Errada@123" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(ResourceErrorMessages.EMAIL_OR_PASSWORD_INVALID, await PedeAquiApiClient.ReadErrors(response));
    }

    [Fact]
    public async Task Login_SeededAdmin_ReturnsToken()
    {
        var api = new PedeAquiApiClient(_factory.CreateClient());

        var response = await api.Login(new LoginRequest
        {
            Email = PedeAquiWebApplicationFactory.ADMIN_EMAIL,
            Password = PedeAquiWebApplicationFactory.ADMIN_PASSWORD
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
