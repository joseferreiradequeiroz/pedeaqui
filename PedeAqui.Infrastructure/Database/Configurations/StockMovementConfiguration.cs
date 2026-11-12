using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedeAqui.Domain.Entities;

namespace PedeAqui.Infrastructure.Database.Configurations;

internal class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");

        builder.Property(movement => movement.Type).HasConversion<string>().HasMaxLength(10);
        builder.Property(movement => movement.Reason).HasMaxLength(200).IsRequired();

        builder.HasOne(movement => movement.Product)
            .WithMany()
            .HasForeignKey(movement => movement.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(movement => movement.Order)
            .WithMany()
            .HasForeignKey(movement => movement.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(movement => new { movement.ProductId, movement.CreatedAt });
    }
}
