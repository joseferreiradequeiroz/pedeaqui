using PedeAqui.Domain.Entities;

namespace PedeAqui.Domain.Repositories.Orders;

public interface IOrderUpdateOnlyRepository
{
    Task<Order?> GetById(User loggedUser, long id, CancellationToken cancellationToken);
    Task<Order?> GetByIdForSystem(long id, CancellationToken cancellationToken);
    Task<List<long>> GetPendingCreatedBefore(DateTime cutoff, int limit, CancellationToken cancellationToken);
    Task<List<long>> GetAwaitingPaymentStartedBefore(DateTime cutoff, int limit, CancellationToken cancellationToken);
    void Update(Order order);
}
