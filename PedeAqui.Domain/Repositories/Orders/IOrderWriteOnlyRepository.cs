using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Orders;

public interface IOrderWriteOnlyRepository
{
    Task Add(Order order);
}
