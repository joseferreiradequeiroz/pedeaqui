using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedeAqui.Domain.Entities;

namespace PedeAqui.Infrastructure.Database.Configurations;

internal class IdempotencyRequestConfiguration : IEntityTypeConfiguration<IdempotencyRequest>
{
    public void Configure(EntityTypeBuilder<IdempotencyRequest> builder)
    {
        builder.ToTable("idempotency_requests");

        builder.Property(request => request.Key).HasMaxLength(IdempotencyRequest.KEY_MAX_LENGTH).IsRequired();
        builder.Property(request => request.Method).HasMaxLength(10).IsRequired();
        builder.Property(request => request.Path).HasMaxLength(500).IsRequired();
        builder.Property(request => request.RequestBody).HasColumnType("jsonb").IsRequired();
        builder.Property(request => request.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(request => request.ResponseBody).HasColumnType("jsonb");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(request => request.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(request => new { request.UserId, request.Key }).IsUnique();
        builder.HasIndex(request => request.ExpiresAt);
    }
}
