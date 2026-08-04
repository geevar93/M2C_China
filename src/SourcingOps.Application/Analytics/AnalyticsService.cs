using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Crm;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Common;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Analytics;

/// <summary>
/// ACTION_PLAN E10-01…E10-07/E10-10, M7 contract. Structurally mirrors
/// <c>InvoiceService.ListAsync</c>/<c>BuildStatusCountsAsync</c> — the house pattern for a
/// filtered aggregate with lookup-keyed zero-fill (E10 rule 1), matched by <c>Code</c> never
/// <c>Label</c> (E10 rule 2, D-50 precedent). All six methods share one <see cref="AnalyticsQuery"/>
/// (E10-07) and cache through <c>ICacheService</c> with a 60s TTL keyed on route+filters
/// (E10-10) — except <see cref="GetInventoryAsync"/>, the one deliberate exception; see its
/// own doc comment.
///
/// SCHEMA GAPS this pass hit and could not silently paper over (no migration in scope):
/// <list type="bullet">
/// <item><description><c>Customer.SourceChannel</c> (leads.bySource) is free text, not an
/// <see cref="ILookupEntity"/> — there is no Code/Label/SortOrder master table for lead
/// source. Zero-fill still applies, but against the set of DISTINCT values ever recorded
/// across all customers (unfiltered), not a configured master list; each channel gets a
/// content-derived deterministic id (<see cref="DeterministicId"/>) since none exists in the
/// schema. See <see cref="GetLeadsAsync"/>.</description></item>
/// <item><description><see cref="Category"/> is NOT an <see cref="ILookupEntity"/> — it has no
/// <c>Code</c> column, only <c>Name</c>. Every category-mix/vendor/inventory breakdown below
/// groups by <c>CategoryId</c> (the stable Guid FK, never a string) and reports
/// <c>Name</c> in both the DTO's <c>code</c> and <c>label</c> slots, ordered by
/// <c>SortOrder</c> then <c>Name</c> ordinal (Code ordinal is unavailable).</description></item>
/// </list>
/// </summary>
public sealed class AnalyticsService : IAnalyticsService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>Default trend window when neither <c>fromDate</c> nor <c>toDate</c> is supplied — 12 whole weeks ending today (not specified by the M7 contract; a reasonable default for a dashboard trend, called out in the build report).</summary>
    private const int DefaultLookbackDays = 83;

    /// <summary>SeedDefaults.LeadStatuses' funnel code for a won lead.</summary>
    private const string LeadWonCode = "WON";

    /// <summary>SeedDefaults.VendorStatuses' code for an active vendor.</summary>
    private const string VendorActiveCode = "ACTIVE";

    /// <summary>SeedDefaults.ShipmentStatuses' code — literal space per the seed (matches the prototype's StatusStyleService key verbatim).</summary>
    private const string ShipmentInTransitCode = "IN TRANSIT";

    /// <summary>SeedDefaults.ShipmentStatuses' terminal code — the only "delivered/closed" status in the seeded lifecycle.</summary>
    private const string ShipmentDeliveredCode = "DELIVERED";

    private readonly IAppDbContext _db;
    private readonly ICacheService _cache;

    public AnalyticsService(IAppDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    // ---- E10-01: Leads -----------------------------------------------------------------

    public Task<LeadsAnalyticsDto> GetLeadsAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        ValidateDateRange(query);
        return _cache.GetOrCreateAsync(BuildCacheKey("leads", query), CacheTtl, token => ComputeLeadsAsync(query, token), ct);
    }

    private async Task<LeadsAnalyticsDto> ComputeLeadsAsync(AnalyticsQuery query, CancellationToken ct)
    {
        var (from, to) = EffectiveRange(query.FromDate, query.ToDate);

        var withoutDate = ApplyLeadNonDateFilters(_db.Customers.AsQueryable(), query);
        var filtered = ApplyCreatedAtRange(withoutDate, from, to);

        var totalLeads = await filtered.CountAsync(ct);
        var wonCount = await filtered.CountAsync(c => c.Status.Code == LeadWonCode, ct);
        var conversionRate = totalLeads == 0 ? 0m : (decimal)wonCount / totalLeads;

        // bySource: Customer.SourceChannel has no lookup table (see class doc comment) — the
        // "master list" is every distinct value ever recorded, independent of this call's
        // filters, so a channel with zero leads THIS period still appears at 0.
        var allChannels = await _db.Customers
            .Select(c => c.SourceChannel)
            .Distinct()
            .ToListAsync(ct);
        var channelCounts = await filtered
            .GroupBy(c => c.SourceChannel)
            .Select(g => new { Channel = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var channelCountsByKey = channelCounts.ToDictionary(x => x.Channel ?? string.Empty, x => x.Count);
        var bySource = allChannels
            .Select(channel => channel is null || string.IsNullOrWhiteSpace(channel) ? string.Empty : channel.Trim())
            .Distinct()
            .OrderBy(channel => channel, StringComparer.Ordinal)
            .Select((channel, index) =>
            {
                var label = channel.Length == 0 ? "Unspecified" : channel;
                var code = channel.Length == 0 ? "UNSPECIFIED" : channel.ToUpperInvariant();
                var count = channelCountsByKey.GetValueOrDefault(channel, 0);
                return new LookupCountDto(DeterministicId(code), code, label, index, count);
            })
            .ToList();

        var leadStatuses = await _db.LeadStatuses.ToListAsync(ct);
        var statusCounts = await filtered
            .GroupBy(c => c.StatusId)
            .Select(g => new { StatusId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var byStatus = ZeroFillLookup(leadStatuses, statusCounts.ToDictionary(x => x.StatusId, x => x.Count));

        var seriesDates = await filtered.Select(c => c.CreatedAt).ToListAsync(ct);
        var series = BuildWeeklySeries(from, to, seriesDates.Select(DateOnly.FromDateTime));

        var lengthDays = to.DayNumber - from.DayNumber;
        var priorTo = from.AddDays(-1);
        var priorFrom = priorTo.AddDays(-lengthDays);
        var priorPeriodCount = await ApplyCreatedAtRange(withoutDate, priorFrom, priorTo).CountAsync(ct);

        return new LeadsAnalyticsDto(series, bySource, byStatus, totalLeads, wonCount, conversionRate, totalLeads, priorPeriodCount);
    }

    /// <summary>categoryId (via CustomerCategories) and serviceTypeId — the two non-date shared filters that map cleanly onto <see cref="Customer"/>.</summary>
    private static IQueryable<Customer> ApplyLeadNonDateFilters(IQueryable<Customer> source, AnalyticsQuery query)
    {
        if (query.CategoryId.HasValue)
        {
            source = source.Where(c => c.CustomerCategories.Any(cc => cc.CategoryId == query.CategoryId.Value));
        }
        if (query.ServiceTypeId.HasValue)
        {
            source = source.Where(c => c.ServiceTypeId == query.ServiceTypeId.Value);
        }
        return source;
    }

    private static IQueryable<Customer> ApplyCreatedAtRange(IQueryable<Customer> source, DateOnly from, DateOnly to)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtcExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return source.Where(c => c.CreatedAt >= fromUtc && c.CreatedAt < toUtcExclusive);
    }

    // ---- E10-02: Service split ----------------------------------------------------------

    public Task<ServiceSplitAnalyticsDto> GetServiceSplitAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        ValidateDateRange(query);
        return _cache.GetOrCreateAsync(BuildCacheKey("service-split", query), CacheTtl, token => ComputeServiceSplitAsync(query, token), ct);
    }

    private async Task<ServiceSplitAnalyticsDto> ComputeServiceSplitAsync(AnalyticsQuery query, CancellationToken ct)
    {
        var (from, to) = EffectiveRange(query.FromDate, query.ToDate);
        var filtered = ApplyCreatedAtRange(ApplyLeadNonDateFilters(_db.Customers.AsQueryable(), query), from, to);

        var totalCustomers = await filtered.CountAsync(ct);

        var serviceTypes = await _db.ServiceTypes.ToListAsync(ct);
        var counts = await filtered
            .GroupBy(c => c.ServiceTypeId)
            .Select(g => new { ServiceTypeId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var items = ZeroFillLookup(serviceTypes, counts.ToDictionary(x => x.ServiceTypeId, x => x.Count))
            .Select(x => new LookupCustomerCountDto(x.Id, x.Code, x.Label, x.SortOrder, x.Count))
            .ToList();

        return new ServiceSplitAnalyticsDto(totalCustomers, items);
    }

    // ---- E10-03: Category mix -------------------------------------------------------------

    public Task<CategoryMixAnalyticsDto> GetCategoryMixAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        ValidateDateRange(query);
        return _cache.GetOrCreateAsync(BuildCacheKey("category-mix", query), CacheTtl, token => ComputeCategoryMixAsync(query, token), ct);
    }

    private async Task<CategoryMixAnalyticsDto> ComputeCategoryMixAsync(AnalyticsQuery query, CancellationToken ct)
    {
        var (from, to) = EffectiveRange(query.FromDate, query.ToDate);
        var filteredCustomerIds = await ApplyCreatedAtRange(ApplyLeadNonDateFilters(_db.Customers.AsQueryable(), query), from, to)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var categories = await _db.Categories.ToListAsync(ct);
        var counts = await _db.CustomerCategories
            .Where(cc => filteredCustomerIds.Contains(cc.CustomerId))
            .GroupBy(cc => cc.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Select(x => x.CustomerId).Distinct().Count() })
            .ToListAsync(ct);
        var countsById = counts.ToDictionary(x => x.CategoryId, x => x.Count);

        var items = OrderedCategories(categories)
            .Select(cat => new LookupCustomerCountDto(cat.Id, cat.Name, cat.Name, cat.SortOrder, countsById.GetValueOrDefault(cat.Id, 0)))
            .ToList();

        return new CategoryMixAnalyticsDto(items);
    }

    // ---- E10-04: Vendors ------------------------------------------------------------------

    public Task<VendorAnalyticsDto> GetVendorsAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        ValidateDateRange(query);
        return _cache.GetOrCreateAsync(BuildCacheKey("vendors", query), CacheTtl, token => ComputeVendorsAsync(query, token), ct);
    }

    /// <summary>
    /// <c>serviceTypeId</c> is a documented no-op here — <see cref="Vendor"/> carries no
    /// service type anywhere in the schema. <c>categoryId</c> filters via <c>VendorCategory</c>;
    /// <c>fromDate</c>/<c>toDate</c> filter on <c>Vendor.CreatedAt</c>. <see cref="VendorCategoryStatDto.CatalogCount"/>
    /// counts <see cref="CatalogSection"/> rows by category directly (its own <c>CreatedAt</c>,
    /// same date range applied) — independent of a vendor's active/on-hold/inactive status,
    /// since a catalog can outlive a vendor's current lifecycle state.
    /// </summary>
    private async Task<VendorAnalyticsDto> ComputeVendorsAsync(AnalyticsQuery query, CancellationToken ct)
    {
        var (from, to) = EffectiveRange(query.FromDate, query.ToDate);
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtcExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var vendors = _db.Vendors.Where(v => v.CreatedAt >= fromUtc && v.CreatedAt < toUtcExclusive);
        if (query.CategoryId.HasValue)
        {
            vendors = vendors.Where(v => v.VendorCategories.Any(vc => vc.CategoryId == query.CategoryId.Value));
        }

        var totalActiveVendors = await vendors.CountAsync(v => v.Status.Code == VendorActiveCode, ct);

        var vendorIds = await vendors.Select(v => v.Id).ToListAsync(ct);
        var vendorCounts = await _db.VendorCategories
            .Where(vc => vendorIds.Contains(vc.VendorId))
            .GroupBy(vc => vc.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Select(x => x.VendorId).Distinct().Count() })
            .ToListAsync(ct);
        var vendorCountsById = vendorCounts.ToDictionary(x => x.CategoryId, x => x.Count);

        var catalogCounts = await _db.CatalogSections
            .Where(cs => cs.CreatedAt >= fromUtc && cs.CreatedAt < toUtcExclusive)
            .GroupBy(cs => cs.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var catalogCountsById = catalogCounts.ToDictionary(x => x.CategoryId, x => x.Count);

        var categories = await _db.Categories.ToListAsync(ct);
        var byCategory = OrderedCategories(categories)
            .Select(cat => new VendorCategoryStatDto(
                cat.Id, cat.Name, cat.Name, cat.SortOrder,
                vendorCountsById.GetValueOrDefault(cat.Id, 0),
                catalogCountsById.GetValueOrDefault(cat.Id, 0)))
            .ToList();

        return new VendorAnalyticsDto(totalActiveVendors, byCategory);
    }

    // ---- E10-05: Inventory (E10-10: NEVER cached) ------------------------------------------

    /// <summary>
    /// Deliberately bypasses <c>ICacheService</c> entirely — TECH_SPEC §4.5 and E10-01's
    /// acceptance criteria withhold caching from anything user-write-adjacent, and this
    /// endpoint surfaces live on-hand stock and live shipment status. Do NOT add caching here;
    /// that is the one named exception in the M7 contract (E10-10), not an oversight.
    /// </summary>
    public async Task<InventoryAnalyticsDto> GetInventoryAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        ValidateDateRange(query);

        // fromDate/toDate/serviceTypeId are documented no-ops against the on-hand snapshot
        // portion below (OnHandValue/ByCategory): InventoryItem carries neither a CreatedAt
        // nor a ServiceType, and on-hand quantity/value is a CURRENT state, not a historical
        // one — there is no meaningful "on-hand value as of a date range" without a stock
        // ledger, which does not exist in this schema. categoryId DOES apply (InventoryItem.CategoryId).
        // The shipment-derived fields below (ShipmentsByStatus/InTransitCount/PastEtaCount) DO
        // honour fromDate/toDate (on Shipment.CreatedAt) and serviceTypeId (Shipment.ServiceTypeId).
        var items = _db.InventoryItems.AsQueryable();
        if (query.CategoryId.HasValue)
        {
            items = items.Where(i => i.CategoryId == query.CategoryId.Value);
        }

        var itemRows = await items.Select(i => new { i.CategoryId, i.OnHandQty, i.UnitCost, i.ReorderThreshold }).ToListAsync(ct);
        var onHandValue = itemRows.Sum(i => i.OnHandQty * (i.UnitCost ?? 0m));

        // Below-reorder count: same categoryId filter as the rest of this on-hand snapshot
        // (see the no-op note above — fromDate/toDate/serviceTypeId don't apply here either).
        // Predicate MUST stay in step with SourcingOps.Application.Inventory.StockLevels.For's
        // "Low" branch (onHandQty < reorderThreshold) — that helper is an in-memory static and
        // can't be pushed into the EF query above, so it is replicated here rather than shared.
        var belowReorderCount = itemRows.Count(i => i.OnHandQty < i.ReorderThreshold);

        var categories = await _db.Categories.ToListAsync(ct);
        var byCategoryRaw = itemRows
            .GroupBy(i => i.CategoryId)
            .ToDictionary(g => g.Key, g => (Quantity: g.Sum(i => i.OnHandQty), Value: g.Sum(i => i.OnHandQty * (i.UnitCost ?? 0m))));
        var byCategory = OrderedCategories(categories)
            .Select(cat =>
            {
                var (quantity, value) = byCategoryRaw.GetValueOrDefault(cat.Id, (0m, 0m));
                return new InventoryCategoryStatDto(cat.Id, cat.Name, cat.Name, cat.SortOrder, quantity, value);
            })
            .ToList();

        var (from, to) = EffectiveRange(query.FromDate, query.ToDate);
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtcExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var shipments = _db.Shipments.Where(s => s.CreatedAt >= fromUtc && s.CreatedAt < toUtcExclusive);
        if (query.ServiceTypeId.HasValue)
        {
            shipments = shipments.Where(s => s.ServiceTypeId == query.ServiceTypeId.Value);
        }

        var shipmentStatuses = await _db.ShipmentStatuses.ToListAsync(ct);
        var statusCounts = await shipments
            .GroupBy(s => s.StatusId)
            .Select(g => new { StatusId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var shipmentsByStatus = ZeroFillLookup(shipmentStatuses, statusCounts.ToDictionary(x => x.StatusId, x => x.Count));

        var inTransitCount = await shipments.CountAsync(s => s.Status.Code == ShipmentInTransitCode, ct);

        // "Past ETA": Eta is set and earlier than today (UTC date), and the shipment has not
        // yet reached the terminal DELIVERED status — DELIVERED is the only "closed" status in
        // the seeded shipment lifecycle (PACKED/DISPATCHED/IN TRANSIT/DELIVERED), so
        // "not yet delivered/closed" is implemented as Code != DELIVERED. Matched by Code, not
        // Label (E10 rule 2).
        var todayUtc = DateTime.UtcNow.Date;
        var pastEtaCount = await shipments.CountAsync(
            s => s.Eta.HasValue && s.Eta.Value < todayUtc && s.Status.Code != ShipmentDeliveredCode, ct);

        return new InventoryAnalyticsDto(onHandValue, byCategory, shipmentsByStatus, inTransitCount, pastEtaCount, belowReorderCount);
    }

    // ---- E10-06: Dispatch -------------------------------------------------------------------

    public Task<DispatchAnalyticsDto> GetDispatchAsync(AnalyticsQuery query, CancellationToken ct = default)
    {
        ValidateDateRange(query);
        return _cache.GetOrCreateAsync(BuildCacheKey("dispatch", query), CacheTtl, token => ComputeDispatchAsync(query, token), ct);
    }

    /// <summary>
    /// <c>categoryId</c>/<c>serviceTypeId</c> apply via the dispatch's <c>Customer</c> (the
    /// same "customer's category interest / service type" semantics <see cref="GetLeadsAsync"/>
    /// uses) — a dispatch has no category of its own. <c>fromDate</c>/<c>toDate</c> filter on
    /// <c>SentAt</c>.
    /// </summary>
    private async Task<DispatchAnalyticsDto> ComputeDispatchAsync(AnalyticsQuery query, CancellationToken ct)
    {
        var (from, to) = EffectiveRange(query.FromDate, query.ToDate);
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtcExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var dispatches = _db.Dispatches.Where(d => d.SentAt >= fromUtc && d.SentAt < toUtcExclusive);
        if (query.CategoryId.HasValue)
        {
            dispatches = dispatches.Where(d => d.Customer.CustomerCategories.Any(cc => cc.CategoryId == query.CategoryId.Value));
        }
        if (query.ServiceTypeId.HasValue)
        {
            dispatches = dispatches.Where(d => d.Customer.ServiceTypeId == query.ServiceTypeId.Value);
        }

        var totalDispatches = await dispatches.CountAsync(ct);

        var seriesDates = await dispatches.Select(d => d.SentAt).ToListAsync(ct);
        var series = BuildWeeklySeries(from, to, seriesDates.Select(DateOnly.FromDateTime));

        var staffRows = await dispatches
            .GroupBy(d => new { d.StaffUserId, d.StaffUser.Name })
            .Select(g => new { g.Key.StaffUserId, g.Key.Name, Count = g.Count() })
            .ToListAsync(ct);
        var byStaff = staffRows
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => new DispatchStaffCountDto(x.StaffUserId, x.Name, x.Count))
            .ToList();

        // byKind: D-67's whole point (E10 rule 3) — always both kinds, zero-filled, computed
        // from CatalogDocumentId/InvoiceId presence exactly as CustomerService.MapDispatchTimelineEvent
        // does, never from a shared/collapsed kind.
        var catalogCount = await dispatches.CountAsync(d => d.CatalogDocumentId != null, ct);
        var invoiceCount = await dispatches.CountAsync(d => d.InvoiceId != null, ct);
        var byKind = new List<DispatchKindCountDto>
        {
            new(TimelineEventKinds.CatalogDispatched, catalogCount),
            new(TimelineEventKinds.InvoiceDispatched, invoiceCount)
        };

        return new DispatchAnalyticsDto(series, byStaff, byKind, totalDispatches);
    }

    // ---- Shared helpers -------------------------------------------------------------------

    private static void ValidateDateRange(AnalyticsQuery query)
    {
        if (query.FromDate.HasValue && query.ToDate.HasValue && query.FromDate.Value > query.ToDate.Value)
        {
            throw new AppValidationException("fromDate", "'fromDate' must not be later than 'toDate'.");
        }
    }

    /// <summary>E10-10: route name + every filter value, so a filtered dashboard is never served to a request with different filters.</summary>
    private static string BuildCacheKey(string route, AnalyticsQuery query) =>
        $"analytics:{route}:{query.FromDate?.ToString("O") ?? "-"}:{query.ToDate?.ToString("O") ?? "-"}:" +
        $"{query.CategoryId?.ToString() ?? "-"}:{query.ServiceTypeId?.ToString() ?? "-"}";

    /// <summary>No range supplied defaults to the trailing <see cref="DefaultLookbackDays"/>+1 days ending today — see class doc comment.</summary>
    private static (DateOnly From, DateOnly To) EffectiveRange(DateOnly? from, DateOnly? to)
    {
        var effectiveTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var effectiveFrom = from ?? effectiveTo.AddDays(-DefaultLookbackDays);
        return (effectiveFrom, effectiveTo);
    }

    private static DateOnly WeekStart(DateOnly d)
    {
        var daysSinceMonday = ((int)d.DayOfWeek + 6) % 7;
        return d.AddDays(-daysSinceMonday);
    }

    /// <summary>Weekly buckets across [from,to] inclusive, zero-filled — a week with no rows still appears.</summary>
    private static List<SeriesPointDto> BuildWeeklySeries(DateOnly from, DateOnly to, IEnumerable<DateOnly> dates)
    {
        var counts = dates.GroupBy(WeekStart).ToDictionary(g => g.Key, g => g.Count());
        var series = new List<SeriesPointDto>();
        var cursor = WeekStart(from);
        var lastWeek = WeekStart(to);
        while (cursor <= lastWeek)
        {
            series.Add(new SeriesPointDto(cursor, counts.GetValueOrDefault(cursor, 0)));
            cursor = cursor.AddDays(7);
        }
        return series;
    }

    /// <summary>E10 rule 1/2: zero-fill against the FULL lookup table, ordered by SortOrder then Code ordinal, matched by Id — mirrors InvoiceService.BuildStatusCountsAsync.</summary>
    private static List<LookupCountDto> ZeroFillLookup<TLookup>(IReadOnlyList<TLookup> lookups, IReadOnlyDictionary<Guid, int> countsById)
        where TLookup : ILookupEntity =>
        lookups
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Code, StringComparer.Ordinal)
            .Select(l => new LookupCountDto(l.Id, l.Code, l.Label, l.SortOrder, countsById.GetValueOrDefault(l.Id, 0)))
            .ToList();

    /// <summary>Category has no Code column (see class doc comment) — ordered by SortOrder then Name ordinal instead.</summary>
    private static List<Category> OrderedCategories(IEnumerable<Category> categories) =>
        categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();

    /// <summary>Stable content-derived id for a schema-less bucket key (Customer.SourceChannel has no real lookup row/id) — same input always yields the same Guid across calls.</summary>
    private static Guid DeterministicId(string seed)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash);
    }
}
