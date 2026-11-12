using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedeAqui.Domain.Entities;

namespace PedeAqui.Infrastructure.Database.Configurations;

internal class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.Property(user => user.Name).HasMaxLength(150).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(200).IsRequired();
        builder.Property(user => user.Password).IsRequired();
        builder.Property(user => user.Role).HasMaxLength(20).IsRequired();

        builder.HasIndex(user => user.Email).IsUnique();
        builder.HasIndex(user => user.UserIdentifier).IsUnique();
    }
}
