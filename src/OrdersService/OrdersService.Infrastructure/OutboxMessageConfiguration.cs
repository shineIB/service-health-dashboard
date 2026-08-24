using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OrdersService.Infrastructure;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.OrderId).IsRequired();
        builder.Property(m => m.EventType).HasMaxLength(100).IsRequired();
        builder.Property(m => m.PayloadJson).IsRequired();
        builder.Property(m => m.CreatedAtUtc).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(2000);

        // Matches OutboxDispatcher's pending-message query exactly (WHERE PublishedAtUtc IS
        // NULL AND FailedAtUtc IS NULL) — a composite index instead of two single-column ones
        // since the two columns are never queried independently of each other.
        builder.HasIndex(m => new { m.PublishedAtUtc, m.FailedAtUtc });
        builder.HasIndex(m => m.OrderId);
    }
}
