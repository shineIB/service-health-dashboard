namespace InventoryService.Domain;

public interface IInventoryRepository
{
    Task AddAsync(InventoryItem item, CancellationToken cancellationToken);
    Task<InventoryItem?> GetByProductIdAsync(Guid productId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryItem>> GetAllAsync(CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
