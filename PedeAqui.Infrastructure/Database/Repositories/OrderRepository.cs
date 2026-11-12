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

    public async Task Add(Order order, CancellationToken cancellationToken)
    {
        await _dbContext.Orders.AddAsync(order, cancellationToken);
    }

    public async Task<List<Order>> GetAll(User loggedUser, CancellationToken cancellationToken)
    {
        return await VisibleTo(loggedUser, _dbContext.Orders.AsNoTracking())
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    async Task<Order?> IOrderReadOnlyRepository.GetById(User loggedUser, long id, CancellationToken cancellationToken)
    {
        return await VisibleTo(loggedUser, _dbContext.Orders.AsNoTracking())
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    async Task<Order?> IOrderUpdateOnlyRepository.GetById(User loggedUser, long id, CancellationToken cancellationToken)
    {
        return await VisibleTo(loggedUser, _dbContext.Orders)
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    public async Task<Order?> GetByIdForSystem(long id, CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    public async Task<List<long>> GetPendingCreatedBefore(DateTime cutoff, int limit, CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.Status == OrderStatus.Pending && order.CreatedAt < cutoff)
            .OrderBy(order => order.CreatedAt)
            .Select(order => order.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<long>> GetAwaitingPaymentStartedBefore(DateTime cutoff, int limit, CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.Status == OrderStatus.AwaitingPayment && order.PaymentStartedAt < cutoff)
            .OrderBy(order => order.PaymentStartedAt)
            .Select(order => order.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
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
