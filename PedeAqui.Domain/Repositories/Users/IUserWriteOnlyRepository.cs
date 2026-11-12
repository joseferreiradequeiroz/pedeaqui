using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Users;

public interface IUserWriteOnlyRepository
{
    Task Add(User user);
}
