using Microsoft.EntityFrameworkCore;
using InventoryService.Domain;

namespace InventoryService.Infrastructure;

public sealed class InventoryDbContext : DbContext
{
    public DbSet<InventoryItem> Items => Set<InventoryItem>();

    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
    }
}
