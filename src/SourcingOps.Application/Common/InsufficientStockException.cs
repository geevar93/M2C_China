namespace SourcingOps.Application.Common;

/// <summary>
/// One item that a stock decrement would have driven below zero (E7-06, deviation D-g).
/// Carries only business-visible facts — item name, SKU, what was asked for, what is on hand.
/// No internal identifier beyond the item's own public id, which the caller already holds.
/// </summary>
public sealed record InsufficientStockDetail(
    Guid InventoryItemId,
    string ItemName,
    string? Sku,
    decimal RequestedQty,
    decimal AvailableQty);

/// <summary>
/// Thrown when a shipment would drive one or more inventory items' on-hand quantity negative
/// and the request did not carry <c>allowNegativeStock: true</c> (E7-06 / FR-INV-04,
/// deviation D-g).
///
/// A distinct exception type rather than an <see cref="AppValidationException"/> because the
/// correct status is 409 Conflict, not 400: the request is well-formed and the caller may
/// legitimately retry it unchanged with the override flag set. The Api layer's
/// <c>ExceptionHandlingMiddleware</c> maps it to an RFC 7807 <c>ProblemDetails</c> carrying
/// <see cref="Items"/> under an <c>insufficientStock</c> extension, which is the same
/// surface-the-conflicting-record shape E4-10's duplicate-phone 409 already uses.
///
/// FSD's wording is "prevent or flag" and E7-06's is "rejected or explicitly flagged" — this
/// is the "prevent/reject" default. The "flag" half is the override path, which lets the
/// decrement through and records the deliberate choice in the audit detail. Both halves are
/// required: the approved prototype's own seed data contains an item at <c>qty: -40</c> with
/// a NEGATIVE badge and a "Negative Stock: 1" stat tile, so negative stock is demonstrably
/// meant to be reachable.
/// </summary>
public sealed class InsufficientStockException : Exception
{
    public IReadOnlyList<InsufficientStockDetail> Items { get; }

    public InsufficientStockException(IReadOnlyList<InsufficientStockDetail> items)
        : base(BuildMessage(items))
    {
        Items = items;
    }

    private static string BuildMessage(IReadOnlyList<InsufficientStockDetail> items)
    {
        var named = string.Join("; ", items.Select(i =>
            $"'{i.ItemName}' requested {i.RequestedQty:0.###}, available {i.AvailableQty:0.###}"));
        return $"Insufficient stock for {items.Count} item(s): {named}.";
    }
}
