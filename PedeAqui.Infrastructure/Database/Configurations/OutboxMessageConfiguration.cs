using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedeAqui.Domain.Entities;

namespace PedeAqui.Infrastructure.Database.Configurations;

internal class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Type).HasMaxLength(200).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.CorrelationId).HasMaxLength(64);
        builder.Property(message => message.LastError).HasMaxLength(2000);

        builder.HasIndex(message => message.OccurredAt)
            .HasFilter("\"ProcessedAt\" IS NULL");
    }
}
