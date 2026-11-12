using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedeAqui.Domain.Entities;

namespace PedeAqui.Infrastructure.Database.Configurations;

internal class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.Property(product => product.Name).HasMaxLength(150).IsRequired();
        builder.Property(product => product.Description).HasMaxLength(1000);
        builder.Property(product => product.Sku).HasMaxLength(50).IsRequired();
        builder.Property(product => product.Price).HasPrecision(18, 2);

        builder.HasIndex(product => product.Sku).IsUnique();
    }
}
