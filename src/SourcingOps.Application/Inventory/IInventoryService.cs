namespace SourcingOps.Application.Inventory;

/// <summary>Application service behind <c>InventoryController</c> (ACTION_PLAN E7-01…E7-04).</summary>
public interface IInventoryService
{
    Task<InventoryListResultDto> ListAsync(InventoryListQuery query, CancellationToken ct = default);

    Task<InventoryItemDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<InventoryItemDto> CreateAsync(CreateInventoryItemRequest request, Guid actorUserId, CancellationToken ct = default);

    Task<InventoryItemDto?> UpdateAsync(Guid id, UpdateInventoryItemRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Returns false when the item does not exist; throws <c>AppValidationException</c> when it is still referenced by a shipment line.</summary>
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>E7-02: increments on-hand quantity and writes the durable inbound entry in one transaction.</summary>
    Task<RecordInboundResultDto?> RecordInboundAsync(Guid id, RecordInboundRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>E7-02: the item's inbound entries, newest first. Null when the item does not exist.</summary>
    Task<InventoryInboundEntryListResultDto?> ListInboundEntriesAsync(Guid id, CancellationToken ct = default);

    /// <summary>N-38: records a physical-count correction and sets on-hand quantity to the counted value in one transaction.</summary>
    Task<RecordAdjustmentResultDto?> RecordAdjustmentAsync(Guid id, RecordAdjustmentRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>N-38: the item's stock-adjustment history, newest first. Null when the item does not exist.</summary>
    Task<InventoryStockAdjustmentListResultDto?> ListStockAdjustmentsAsync(Guid id, CancellationToken ct = default);

    /// <summary>Stores (or replaces) the item's image plus its client-generated thumbnail. Null means item not found.</summary>
    Task<InventoryItemDto?> SetImageAsync(Guid id, Stream image, long imageSize, byte[] thumbnail, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Removes the item's image and thumbnail. Null means item not found.</summary>
    Task<InventoryItemDto?> RemoveImageAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Null when the item or its image does not exist.</summary>
    Task<InventoryImageDownload?> GetImageAsync(Guid id, CancellationToken ct = default);
}
