using Microsoft.EntityFrameworkCore;
using PedeAqui.Domain.Entities;

namespace PedeAqui.Infrastructure.Database;

internal class PedeAquiDbContext : DbContext
{
    public PedeAquiDbContext(DbContextOptions<PedeAquiDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<IdempotencyRequest> IdempotencyRequests { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<InboxMessage> InboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PedeAquiDbContext).Assembly);
    }
}
