using SourcingOps.Application.Common;

namespace SourcingOps.Application.Inventory;

/// <summary>
/// The stock-level flag E7-04 requires the API to return, so no screen re-derives it.
/// Values match the approved prototype's own <c>invRows()</c> classification verbatim:
/// negative first, then below-reorder, then healthy.
/// </summary>
public static class StockLevels
{
    public const string Healthy = "HEALTHY";
    public const string Low = "LOW";
    public const string Negative = "NEGATIVE";

    public static string For(decimal onHandQty, decimal reorderThreshold) =>
        onHandQty < 0 ? Negative
        : onHandQty < reorderThreshold ? Low
        : Healthy;
}

/// <summary>Accepted values for <c>GET /inventory?stockLevel=</c> (E7-03).</summary>
public static class StockLevelFilters
{
    public const string All = "all";

    /// <summary>
    /// Below reorder threshold <b>or</b> negative — one option, matching the prototype's
    /// single "Low or negative" dropdown entry and its <c>i.qty &lt; i.reorder</c> predicate.
    /// Deliberately not two separate filters: the screen has one.
    /// </summary>
    public const string Low = "low";

    public const string Healthy = "healthy";

    public static bool IsValid(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Equals(All, StringComparison.OrdinalIgnoreCase) ||
        value.Equals(Low, StringComparison.OrdinalIgnoreCase) ||
        value.Equals(Healthy, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One inventory row (E7-01/E7-03/E7-04). <see cref="Category"/> and <see cref="Vendor"/> are
/// embedded resolved lookup objects, matching the M4 vendor/catalog convention this track
/// follows rather than the CRM track's bare-id convention (see RefDtos.cs).
///
/// <see cref="StockValue"/> is always COMPUTED (<c>OnHandQty * UnitCost</c>) and never stored
/// — <c>null</c> when the item has no <see cref="UnitCost"/>, so the screen can distinguish
/// "worth nothing" from "not costed yet". <see cref="OnHandQty"/> and
/// <see cref="ReorderThreshold"/> are returned raw alongside <see cref="StockLevel"/> so the
/// prototype's visual bar can do its ratio maths client-side, which is presentation and stays
/// in the Angular component (E7-04).
/// </summary>
public sealed record InventoryItemDto(
    Guid Id,
    string Name,
    string? Sku,
    string? Description,
    CategoryRefDto Category,
    VendorRefDto? Vendor,
    string Unit,
    decimal OnHandQty,
    decimal ReorderThreshold,
    decimal? UnitCost,
    decimal? StockValue,
    string StockLevel);

/// <summary>
/// The four stat tiles the approved inventory screen renders above the table (deviation
/// D-k). Computed over the WHOLE filtered set, not the current page — a tile that changed
/// when you paged would be wrong.
///
/// This deliberately duplicates a little of what E10-05 (analytics, M7) will later aggregate.
/// That is a recorded choice: E10-05 aggregates on-hand quantity/value BY CATEGORY across the
/// business, whereas this is screen-local and must honour the caller's active filters, so
/// neither can serve the other's job without a parameter both would rather not have.
/// </summary>
public sealed record InventorySummaryDto(
    decimal OnHandValue,
    int ItemCount,
    int LowStockCount,
    int NegativeStockCount);

public sealed record InventoryListResultDto(
    IReadOnlyList<InventoryItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    InventorySummaryDto Summary);

/// <summary>A recorded inbound stock receipt (E7-02, deviation D-d).</summary>
public sealed record InventoryInboundEntryDto(
    Guid Id,
    Guid InventoryItemId,
    decimal Quantity,
    DateOnly EntryDate,
    string? Reference,
    Guid RecordedByUserId,
    string RecordedByName,
    DateTime CreatedAt);

public sealed record InventoryInboundEntryListResultDto(IReadOnlyList<InventoryInboundEntryDto> Items);

/// <summary>
/// Returned by <c>POST /inventory/{id}/inbound</c>. Carries the item back alongside the new
/// entry so the caller sees the resulting <c>onHandQty</c>/<c>stockLevel</c> without a second
/// round trip — the screen has to re-render both.
/// </summary>
public sealed record RecordInboundResultDto(InventoryInboundEntryDto Entry, InventoryItemDto Item);

public sealed record CreateInventoryItemRequest(
    string Name,
    string? Sku,
    string? Description,
    Guid CategoryId,
    Guid? VendorId,
    string? Unit,
    decimal? OnHandQty,
    decimal? ReorderThreshold,
    decimal? UnitCost);

/// <summary>
/// Deliberately has NO <c>OnHandQty</c>. Stock moves only through the two recorded paths —
/// an inbound entry (E7-02) or a shipment line (E7-06) — so a plain edit can never silently
/// rewrite a balance that an inbound entry or a shipment is the audit record for. An opening
/// balance is settable once, at create. See the M5 build report's deviation list; the stock-take
/// correction case this leaves unserved is recorded there as an open item.
/// </summary>
public sealed record UpdateInventoryItemRequest(
    string Name,
    string? Sku,
    string? Description,
    Guid CategoryId,
    Guid? VendorId,
    string? Unit,
    decimal? ReorderThreshold,
    decimal? UnitCost);

/// <summary>
/// E7-02. <see cref="EntryDate"/> defaults to today (UTC) when omitted;
/// <see cref="Reference"/> is optional, matching E7-02's "with date and reference" without
/// making a supplier reference mandatory for a receipt that has none.
/// </summary>
public sealed record RecordInboundRequest(
    decimal Quantity,
    DateOnly? EntryDate,
    string? Reference);

/// <summary>A recorded physical-count correction (N-38). <see cref="PreviousQty"/> and <see cref="Delta"/> are stored, not computed — see <c>InventoryStockAdjustment</c>'s doc comment.</summary>
public sealed record InventoryStockAdjustmentDto(
    Guid Id,
    Guid InventoryItemId,
    decimal CountedQty,
    decimal PreviousQty,
    decimal Delta,
    string Reason,
    DateOnly AdjustedOn,
    DateTime AdjustedAt,
    Guid AdjustedByUserId,
    string AdjustedByName);

public sealed record InventoryStockAdjustmentListResultDto(IReadOnlyList<InventoryStockAdjustmentDto> Items);

/// <summary>
/// Returned by <c>POST /inventory/{id}/adjustments</c>. Carries the item back alongside the new
/// adjustment record, same reason <see cref="RecordInboundResultDto"/> does for inbound entries —
/// the caller re-renders both <c>onHandQty</c> and the history in one round trip.
/// </summary>
public sealed record RecordAdjustmentResultDto(InventoryStockAdjustmentDto Adjustment, InventoryItemDto Item);

/// <summary>
/// N-38. <see cref="AdjustedOn"/> defaults to today (UTC) when omitted, matching
/// <see cref="RecordInboundRequest.EntryDate"/>'s convention. <see cref="Reason"/> is
/// REQUIRED (unlike <see cref="RecordInboundRequest.Reference"/>) — a stock correction with no
/// stated reason is precisely the audit hole this feature exists to close.
/// </summary>
public sealed record RecordAdjustmentRequest(
    decimal CountedQty,
    string Reason,
    DateOnly? AdjustedOn);

/// <summary>E7-03: search (name + sku) plus the three named filters and paging.</summary>
public sealed record InventoryListQuery(
    string? Search,
    int Page,
    int PageSize,
    Guid? CategoryId,
    Guid? VendorId,
    string? StockLevel);
