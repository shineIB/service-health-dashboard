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

        // OutboxDispatcher's poll query filters on this — see ServiceCollectionExtensions/
        // OutboxDispatcher.cs.
        builder.HasIndex(m => m.PublishedAtUtc);
        builder.HasIndex(m => m.OrderId);
    }
}
