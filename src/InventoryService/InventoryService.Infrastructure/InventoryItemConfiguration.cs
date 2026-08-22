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

        builder.OwnsMany(i => i.Reservations, reservations =>
        {
            reservations.ToTable("Reservations");
            reservations.WithOwner().HasForeignKey("ProductId");

            // A store-generated shadow key, not the natural {ProductId, OrderId} key: with a
            // fully client-set composite key, EF Core can't tell a brand-new Reservation from
            // an existing one to update (both key values are always non-default), and emits an
            // UPDATE that matches zero rows instead of an INSERT. The shadow int starts at the
            // CLR default, which EF reads as "this is new" — the same trick OrderLine uses.
            // {ProductId, OrderId} is still enforced unique at the DB level below.
            reservations.Property<int>("Id");
            reservations.HasKey("Id");
            reservations.HasIndex("ProductId", nameof(Reservation.OrderId)).IsUnique();

            reservations.Property(r => r.OrderId).IsRequired();
            reservations.Property(r => r.Quantity).IsRequired();
            reservations.Property(r => r.CreatedAtUtc).IsRequired();
            reservations.Property(r => r.ExpiresAtUtc).IsRequired();
        });
    }
}
