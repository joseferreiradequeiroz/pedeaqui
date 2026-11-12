using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Domain.Repositories.Orders;

namespace PedeAqui.UnitTests.Fakes;

public sealed class InMemoryOrderRepository : IOrderReadOnlyRepository, IOrderWriteOnlyRepository, IOrderUpdateOnlyRepository
{
    private readonly List<Order> _orders = [];
    private long _nextId = 1;

    public InMemoryOrderRepository(params Order[] orders)
    {
        foreach (var order in orders)
            Store(order);
    }

    public IReadOnlyList<Order> Orders => _orders;
    public Action<Order>? WhenLoadedForSystem { get; set; }

    public Task Add(Order order, CancellationToken cancellationToken)
    {
        Store(order);
        return Task.CompletedTask;
    }

    public Task<List<Order>> GetAll(User loggedUser, CancellationToken cancellationToken) => Task.FromResult(VisibleTo(loggedUser).ToList());

    public Task<Order?> GetById(User loggedUser, long id, CancellationToken cancellationToken) => Task.FromResult(VisibleTo(loggedUser).FirstOrDefault(order => order.Id == id));

    public Task<Order?> GetByIdForSystem(long id, CancellationToken cancellationToken)
    {
        var order = _orders.FirstOrDefault(order => order.Id == id);

        if (order is not null)
            WhenLoadedForSystem?.Invoke(order);

        return Task.FromResult(order);
    }

    public Task<List<long>> GetPendingCreatedBefore(DateTime cutoff, int limit, CancellationToken cancellationToken)
    {
        var ids = _orders
            .Where(order => order.Status == OrderStatus.Pending && order.CreatedAt < cutoff)
            .OrderBy(order => order.CreatedAt)
            .Take(limit)
            .Select(order => order.Id)
            .ToList();

        return Task.FromResult(ids);
    }

    public Task<List<long>> GetAwaitingPaymentStartedBefore(DateTime cutoff, int limit, CancellationToken cancellationToken)
    {
        var ids = _orders
            .Where(order => order.Status == OrderStatus.AwaitingPayment && order.PaymentStartedAt < cutoff)
            .OrderBy(order => order.PaymentStartedAt)
            .Take(limit)
            .Select(order => order.Id)
            .ToList();

        return Task.FromResult(ids);
    }

    public void Update(Order order)
    {
    }

    private IEnumerable<Order> VisibleTo(User loggedUser)
    {
        return loggedUser.Role == Roles.ADMIN ? _orders : _orders.Where(order => order.UserId == loggedUser.Id);
    }

    private void Store(Order order)
    {
        if (order.Id == 0)
            order.Id = _nextId;

        _nextId = Math.Max(_nextId, order.Id) + 1;
        _orders.Add(order);
    }
}
