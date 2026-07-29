namespace SourcingOps.Application.Shipments;

/// <summary>Application service behind <c>ShipmentsController</c> (ACTION_PLAN E7-05…E7-08, E7-10).</summary>
public interface IShipmentService
{
    Task<ShipmentListResultDto> ListAsync(ShipmentListQuery query, CancellationToken ct = default);

    Task<ShipmentDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Throws <c>InsufficientStockException</c> (→ 409) unless the request allows negative stock.</summary>
    Task<ShipmentDetailDto> CreateAsync(CreateShipmentRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Applies the line delta to on-hand quantities (D-j). Throws <c>InsufficientStockException</c> (→ 409) unless the request allows negative stock.</summary>
    Task<ShipmentDetailDto?> UpdateAsync(Guid id, UpdateShipmentRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Restores the stock the shipment's lines consumed, then deletes it. False when not found.</summary>
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>E7-07: writes both a <c>shipment_status_history</c> row and an audit entry. Null when the shipment does not exist.</summary>
    Task<ShipmentDetailDto?> ChangeStatusAsync(Guid id, ChangeShipmentStatusRequest request, Guid actorUserId, CancellationToken ct = default);
}
