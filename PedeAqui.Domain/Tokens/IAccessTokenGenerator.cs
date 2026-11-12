using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Tokens;

public interface IAccessTokenGenerator
{
    string Generate(User user);
}
