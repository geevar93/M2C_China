using SourcingOps.Application.Common;

namespace SourcingOps.Application.Shipments;

/// <summary>
/// One shipment line (E7-05). <see cref="UnitCost"/> is the value SNAPSHOTTED at line
/// creation (deviation D-b), not the item's current cost — see <c>ShipmentLine.UnitCost</c>'s
/// doc comment. <see cref="LineTotal"/> is computed (<c>Quantity * UnitCost</c>) and never
/// stored; null when the line has no unit cost, so the screen can tell "free" from "not
/// costed".
/// </summary>
public sealed record ShipmentLineDto(
    Guid Id,
    Guid InventoryItemId,
    string InventoryItemName,
    string? InventoryItemSku,
    string Unit,
    decimal Quantity,
    decimal? UnitCost,
    decimal? LineTotal);

/// <summary>
/// One timestamped status transition (E7-07, deviation D-e). The approved shipment detail
/// screen's 4-step stepper renders a <c>when</c> under each step from exactly this.
/// </summary>
public sealed record ShipmentStatusHistoryDto(
    Guid Id,
    StatusRefDto Status,
    Guid ChangedByUserId,
    string ChangedByName,
    DateTime ChangedAt,
    string? Note);

/// <summary>
/// A shipment reference document (E7-09). Never exposes <c>FilePath</c> — the client only
/// ever gets an id to hand to the authenticated download endpoint, matching
/// <c>VendorDocumentDto</c>/<c>CatalogDocumentDto</c>'s convention exactly.
/// </summary>
public sealed record ShipmentDocumentDto(
    Guid Id,
    Guid ShipmentId,
    string OriginalFilename,
    long SizeBytes,
    StatusRefDto DocumentType,
    Guid UploadedByUserId,
    string UploadedByName,
    DateTime UploadedAt);

public sealed record ShipmentDocumentListResultDto(IReadOnlyList<ShipmentDocumentDto> Items);

/// <summary>Carries the open stream + metadata the authenticated download endpoint needs without exposing <c>FilePath</c> beyond the service boundary.</summary>
public sealed record ShipmentDocumentDownload(Stream Content, string OriginalFilename, string ContentType);

/// <summary>
/// A shipment list row (E7-08). Embeds resolved lookup objects for service type and status,
/// matching the M4 vendor/catalog convention this track follows.
/// </summary>
public sealed record ShipmentListItemDto(
    Guid Id,
    string? Reference,
    CustomerRefDto Customer,
    string? Destination,
    StatusRefDto ServiceType,
    DateTime? DispatchDate,
    StatusRefDto Status,
    decimal? FreightCost,
    decimal? TotalValue,
    string? Mode,
    string? AwbOrBl,
    DateTime? Eta,
    int LineCount);

/// <summary>
/// Shipment list row + creation timestamp, lines, status history and reference documents —
/// one call, no second round trip, matching <c>VendorDetailDto</c>'s embed-the-children shape.
///
/// <see cref="RecordedBy"/> is the user on the EARLIEST status-history row, i.e. whoever
/// created the shipment. The approved detail screen shows a "Recorded by" field and
/// <c>shipments</c> has no <c>created_by_user_id</c> column; rather than add one, this reads
/// the fact that D-e's history table already stores.
/// </summary>
public sealed record ShipmentDetailDto(
    Guid Id,
    string? Reference,
    CustomerRefDto Customer,
    string? Destination,
    StatusRefDto ServiceType,
    DateTime? DispatchDate,
    StatusRefDto Status,
    decimal? FreightCost,
    decimal? TotalValue,
    string? Mode,
    string? AwbOrBl,
    DateTime? Eta,
    int LineCount,
    DateTime CreatedAt,
    string? RecordedByName,
    IReadOnlyList<ShipmentLineDto> Lines,
    IReadOnlyList<ShipmentStatusHistoryDto> StatusHistory,
    IReadOnlyList<ShipmentDocumentDto> Documents);

/// <summary>
/// Per-status count across the WHOLE filtered set excluding the status filter itself —
/// E7-08's "prototype's status tabs, which show a count per status across the whole set".
/// Counting inside the status filter would make every tab read either its own total or zero.
/// </summary>
public sealed record ShipmentStatusCountDto(
    Guid StatusId,
    string Code,
    string Label,
    int SortOrder,
    int Count);

public sealed record ShipmentListResultDto(
    IReadOnlyList<ShipmentListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<ShipmentStatusCountDto> StatusCounts);

/// <summary>
/// One requested line. <see cref="UnitCost"/> is honoured when supplied and defaulted from
/// the inventory item's own <c>UnitCost</c> when omitted (deviation D-b).
/// </summary>
public sealed record ShipmentLineRequest(
    Guid InventoryItemId,
    decimal Quantity,
    decimal? UnitCost);

/// <summary>
/// E7-05/E7-06/E7-10. <see cref="Reference"/> is absent by design — it is generated
/// server-side as <c>SHP-YYMM-NNN</c> and never accepted from the caller (deviation D-i).
///
/// <see cref="TotalValue"/> is accepted only when the shipment has no lines (the freight-only
/// case); with lines present it is server-computed from them and any supplied value is
/// ignored (deviation D-c).
///
/// <see cref="AllowNegativeStock"/> defaults to false: a shipment that would drive stock
/// negative is rejected with 409. Setting it true permits the decrement and records the
/// deliberate override in the audit detail (deviation D-g).
/// </summary>
public sealed record CreateShipmentRequest(
    Guid CustomerId,
    string? Destination,
    Guid ServiceTypeId,
    DateTime? DispatchDate,
    Guid StatusId,
    decimal? FreightCost,
    decimal? TotalValue,
    string? Mode,
    string? AwbOrBl,
    DateTime? Eta,
    IReadOnlyList<ShipmentLineRequest>? Lines,
    bool AllowNegativeStock = false);

/// <summary>
/// Deliberately has NO <c>StatusId</c>. Status moves only through
/// <c>PUT /shipments/{id}/status</c>, which is the one path that writes a
/// <c>shipment_status_history</c> row (E7-07 / D-e) — letting a general edit set status too
/// would produce transitions with no history entry and an empty stepper.
///
/// <see cref="Lines"/> REPLACES the existing line set; the difference is applied to on-hand
/// quantities in one transaction (deviation D-j), never re-decremented from scratch.
/// </summary>
public sealed record UpdateShipmentRequest(
    Guid CustomerId,
    string? Destination,
    Guid ServiceTypeId,
    DateTime? DispatchDate,
    decimal? FreightCost,
    decimal? TotalValue,
    string? Mode,
    string? AwbOrBl,
    DateTime? Eta,
    IReadOnlyList<ShipmentLineRequest>? Lines,
    bool AllowNegativeStock = false);

/// <summary>E7-07. Any status in the lookup may be set; transitioning to the status already held is rejected.</summary>
public sealed record ChangeShipmentStatusRequest(Guid StatusId, string? Note);

/// <summary>E7-08: status/customer/date-range filters plus search on reference and paging.</summary>
public sealed record ShipmentListQuery(
    string? Search,
    int Page,
    int PageSize,
    Guid? StatusId,
    Guid? CustomerId,
    DateTime? From,
    DateTime? To);
