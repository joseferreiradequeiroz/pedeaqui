using Microsoft.EntityFrameworkCore;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;
using PedeAqui.Domain.Repositories.Orders;

namespace PedeAqui.Infrastructure.Database.Repositories;

internal class OrderRepository : IOrderReadOnlyRepository, IOrderWriteOnlyRepository, IOrderUpdateOnlyRepository
{
    private readonly PedeAquiDbContext _dbContext;

    public OrderRepository(PedeAquiDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task Add(Order order)
    {
        await _dbContext.Orders.AddAsync(order);
    }

    public async Task<List<Order>> GetAll(User loggedUser)
    {
        return await VisibleTo(loggedUser, _dbContext.Orders.AsNoTracking())
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync();
    }

    async Task<Order?> IOrderReadOnlyRepository.GetById(User loggedUser, long id)
    {
        return await VisibleTo(loggedUser, _dbContext.Orders.AsNoTracking())
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id);
    }

    async Task<Order?> IOrderUpdateOnlyRepository.GetById(User loggedUser, long id)
    {
        return await VisibleTo(loggedUser, _dbContext.Orders)
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id);
    }

    public void Update(Order order)
    {
        _dbContext.Orders.Update(order);
    }

    private static IQueryable<Order> VisibleTo(User loggedUser, IQueryable<Order> query)
    {
        return loggedUser.Role == Roles.ADMIN
            ? query
            : query.Where(order => order.UserId == loggedUser.Id);
    }
}
