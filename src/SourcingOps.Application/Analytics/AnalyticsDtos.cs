namespace SourcingOps.Application.Analytics;

/// <summary>
/// M7 contract §E10-07: ONE shared query shape across all six analytics endpoints — never
/// six near-copies. All four fields are optional. <c>FromDate</c>/<c>ToDate</c> are business
/// dates (no time component); <c>CategoryId</c>/<c>ServiceTypeId</c> are lookup FKs. Not every
/// field is meaningful on every endpoint (e.g. <c>ServiceTypeId</c> has no mapping onto
/// <c>Vendor</c>, which carries no service type) — where a filter does not apply to a given
/// aggregate's domain it is a documented no-op there, per each method's own doc comment,
/// rather than silently dropped from the shared record.
/// </summary>
public sealed record AnalyticsQuery(DateOnly? FromDate, DateOnly? ToDate, Guid? CategoryId, Guid? ServiceTypeId);

/// <summary>One point on a weekly trend line. Buckets are zero-filled across the whole effective range — never omitted for having no rows that week.</summary>
public sealed record SeriesPointDto(DateOnly PeriodStart, int Count);

/// <summary>
/// A lookup-keyed count (E10 rule 1/2): zero-filled against the FULL master table, ordered by
/// <c>SortOrder</c> then <c>Code</c> ordinal, and always matched/grouped by the lookup's
/// <c>Id</c> — never by <c>Label</c> (D-50 precedent). Shared across every endpoint whose
/// breakdown is "count of X per lookup row" (leads.bySource, leads.byStatus,
/// inventory.shipmentsByStatus).
/// </summary>
public sealed record LookupCountDto(Guid Id, string Code, string Label, int SortOrder, int Count);

/// <summary>Same zero-fill/ordering/match-by-id contract as <see cref="LookupCountDto"/>, but counting distinct customers rather than rows (service-split.items, category-mix.items).</summary>
public sealed record LookupCustomerCountDto(Guid Id, string Code, string Label, int SortOrder, int CustomerCount);

public sealed record LeadsAnalyticsDto(
    IReadOnlyList<SeriesPointDto> Series,
    IReadOnlyList<LookupCountDto> BySource,
    IReadOnlyList<LookupCountDto> ByStatus,
    int TotalLeads,
    int WonCount,
    decimal ConversionRate,
    int CurrentPeriodCount,
    int PriorPeriodCount);

public sealed record ServiceSplitAnalyticsDto(int TotalCustomers, IReadOnlyList<LookupCustomerCountDto> Items);

public sealed record CategoryMixAnalyticsDto(IReadOnlyList<LookupCustomerCountDto> Items);

/// <summary><see cref="VendorCount"/> is distinct vendors in that category; <see cref="CatalogCount"/> is catalog sections tagged to it — see AnalyticsService.GetVendorsAsync's doc comment for how each is filtered.</summary>
public sealed record VendorCategoryStatDto(Guid Id, string Code, string Label, int SortOrder, int VendorCount, int CatalogCount);

public sealed record VendorAnalyticsDto(int TotalActiveVendors, IReadOnlyList<VendorCategoryStatDto> ByCategory);

public sealed record InventoryCategoryStatDto(Guid Id, string Code, string Label, int SortOrder, decimal Quantity, decimal Value);

/// <summary>
/// E10-05/E10-10: deliberately NEVER cached (see AnalyticsService.GetInventoryAsync's doc
/// comment) — it carries live stock and live shipment status, the one exception to E10-10's
/// otherwise-blanket 60s cache.
/// </summary>
public sealed record InventoryAnalyticsDto(
    decimal OnHandValue,
    IReadOnlyList<InventoryCategoryStatDto> ByCategory,
    IReadOnlyList<LookupCountDto> ShipmentsByStatus,
    int InTransitCount,
    int PastEtaCount);

public sealed record DispatchStaffCountDto(Guid UserId, string Name, int Count);

/// <summary><see cref="Kind"/> is one of <c>TimelineEventKinds.CatalogDispatched</c>/<c>InvoiceDispatched</c> — D-67 split them specifically so this pair stays countable apart (E10 rule 3).</summary>
public sealed record DispatchKindCountDto(string Kind, int Count);

public sealed record DispatchAnalyticsDto(
    IReadOnlyList<SeriesPointDto> Series,
    IReadOnlyList<DispatchStaffCountDto> ByStaff,
    IReadOnlyList<DispatchKindCountDto> ByKind,
    int TotalDispatches);
