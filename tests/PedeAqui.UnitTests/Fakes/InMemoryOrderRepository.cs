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

    public Task Add(Order order)
    {
        Store(order);
        return Task.CompletedTask;
    }

    public Task<List<Order>> GetAll(User loggedUser) => Task.FromResult(VisibleTo(loggedUser).ToList());

    public Task<Order?> GetById(User loggedUser, long id) => Task.FromResult(VisibleTo(loggedUser).FirstOrDefault(order => order.Id == id));

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
