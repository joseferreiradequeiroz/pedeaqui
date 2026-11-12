using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Orders;

public interface IOrderUpdateOnlyRepository
{
    Task<Order?> GetById(User loggedUser, long id);
    void Update(Order order);
}
