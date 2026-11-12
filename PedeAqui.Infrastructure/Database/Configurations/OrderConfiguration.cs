using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedeAqui.Domain.Entities;

namespace PedeAqui.Infrastructure.Database.Configurations;

internal class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(order => order.Total).HasPrecision(18, 2);
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasOne(order => order.User)
            .WithMany()
            .HasForeignKey(order => order.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(order => order.UserId);
    }
}

internal class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");

        builder.Property(item => item.ProductName).HasMaxLength(150).IsRequired();
        builder.Property(item => item.UnitPrice).HasPrecision(18, 2);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
