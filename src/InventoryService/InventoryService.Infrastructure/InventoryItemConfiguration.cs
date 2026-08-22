using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InventoryService.Domain;

namespace InventoryService.Infrastructure;

public sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("InventoryItems");
        builder.HasKey(i => i.ProductId);

        builder.Property(i => i.AvailableQuantity).IsRequired();
        builder.Property(i => i.ReservedQuantity).IsRequired();
    }
}
