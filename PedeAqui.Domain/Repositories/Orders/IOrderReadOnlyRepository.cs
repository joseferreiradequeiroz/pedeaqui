using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Orders;

public interface IOrderReadOnlyRepository
{
    Task<List<Order>> GetAll(User loggedUser, CancellationToken cancellationToken);
    Task<Order?> GetById(User loggedUser, long id, CancellationToken cancellationToken);
}
