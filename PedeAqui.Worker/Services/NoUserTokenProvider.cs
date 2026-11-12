using PedeAqui.Domain.Tokens;

namespace PedeAqui.Worker.Services;

public sealed class NoUserTokenProvider : ITokenProvider
{
    public string TokenOnRequest() =>
        throw new InvalidOperationException("The worker processes messages, not user requests, so there is no logged user token.");
}
