using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Services;

public interface ILoggedUser
{
    Task<User> Get();
}
