using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Common;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.MasterData;

/// <summary>
/// Implements ACTION_PLAN E3-03…E3-09. <see cref="Category"/> is the one entity that does
/// not implement <see cref="ILookupEntity"/> (<c>Name</c> instead of <c>Code</c>/<c>Label</c>
/// — TECH_SPEC §6), so it gets its own small set of private methods; every other collection
/// (ServiceType/LeadStatus/ShipmentStatus/InvoiceStatus/VendorStatus) shares ONE generic
/// implementation per operation, parameterized over the concrete <c>DbSet&lt;TEntity&gt;</c>,
/// so there are not six copies of the same create/update/retire/reorder/delete logic — per
/// the coordinator's explicit brief.
///
/// PUT (update) only ever edits the display text — <c>Name</c> for categories, <c>Label</c>
/// for everything else — never <c>Code</c>. This reads directly from the binding contract's
/// own wording ("rename / edit label"): categories only have a name to rename; the other five
/// collections keep their `Code` fixed after creation and only allow relabeling. This also
/// protects the frontend's `StatusStyleService`/`SVC`/`ST` colour maps, which key off `Code`
/// verbatim (e.g. "IN TRANSIT") — silently allowing `Code` edits would desynchronize those
/// maps without any code change on either side.
///
/// Every query used for uniqueness/reference/id-existence checks operates on an already
/// materialized `List&lt;TEntity&gt;` (an in-memory LINQ-to-Objects filter), not a LINQ
/// expression built over the generic `TEntity : ILookupEntity` constraint — EF Core's
/// expression-tree translator is not guaranteed to resolve a predicate written against an
/// interface member back to the concrete mapped column for an arbitrary closed generic type.
/// These lookup tables are seeded with a handful of rows each (TECH_SPEC §6), so loading a
/// whole collection per operation has no measurable cost — this is a correctness choice, not
/// a performance one. Point lookups by primary key use `DbSet&lt;TEntity&gt;.FindAsync`,
/// which resolves via the model's key metadata directly and has no such translation risk.
/// </summary>
public sealed class MasterDataService : IMasterDataService
{
    private const string AggregateActiveCacheKey = "masterdata:aggregate:active";
    private const string AggregateAllCacheKey = "masterdata:aggregate:all";

    // Invalidated explicitly on every write (E3-09) — the TTL only bounds staleness if an
    // invalidation call is ever missed; it is not the primary freshness mechanism.
    private static readonly TimeSpan AggregateCacheTtl = TimeSpan.FromHours(6);

    private readonly IAppDbContext _db;
    private readonly ICacheService _cache;
    private readonly IAuditLogger _audit;

    public MasterDataService(IAppDbContext db, ICacheService cache, IAuditLogger audit)
    {
        _db = db;
        _cache = cache;
        _audit = audit;
    }

    // ---- Aggregate read (E3-09's first real cache consumer) ---------------

    public Task<MasterDataAggregateDto> GetAggregateAsync(bool includeRetired, CancellationToken ct = default)
    {
        var cacheKey = includeRetired ? AggregateAllCacheKey : AggregateActiveCacheKey;
        return _cache.GetOrCreateAsync(cacheKey, AggregateCacheTtl, token => LoadAggregateAsync(includeRetired, token), ct);
    }

    private async Task<MasterDataAggregateDto> LoadAggregateAsync(bool includeRetired, CancellationToken ct)
    {
        var categories = await _db.Categories.ToListAsync(ct);
        var serviceTypes = await _db.ServiceTypes.ToListAsync(ct);
        var leadStatuses = await _db.LeadStatuses.ToListAsync(ct);
        var shipmentStatuses = await _db.ShipmentStatuses.ToListAsync(ct);
        var invoiceStatuses = await _db.InvoiceStatuses.ToListAsync(ct);
        var vendorStatuses = await _db.VendorStatuses.ToListAsync(ct);

        return new MasterDataAggregateDto(
            FilterAndMapCategories(categories, includeRetired),
            FilterAndMapLookup(serviceTypes, includeRetired),
            FilterAndMapLookup(leadStatuses, includeRetired),
            FilterAndMapLookup(shipmentStatuses, includeRetired),
            FilterAndMapLookup(invoiceStatuses, includeRetired),
            FilterAndMapLookup(vendorStatuses, includeRetired));
    }

    private static IReadOnlyList<CategoryDto> FilterAndMapCategories(List<Category> all, bool includeRetired) =>
        all.Where(c => includeRetired || c.IsActive)
           .OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.Ordinal)
           .Select(c => ToDto(c))
           .ToList();

    private static IReadOnlyList<LookupItemDto> FilterAndMapLookup<TEntity>(List<TEntity> all, bool includeRetired)
        where TEntity : ILookupEntity =>
        all.Where(e => includeRetired || e.IsActive)
           .OrderBy(e => e.SortOrder).ThenBy(e => e.Code, StringComparer.Ordinal)
           .Select(e => ToDto(e))
           .ToList();

    private static CategoryDto ToDto(Category c) => new(c.Id, c.Name, c.SortOrder, c.IsActive);
    private static LookupItemDto ToDto(ILookupEntity e) => new(e.Id, e.Code, e.Label, e.SortOrder, e.IsActive);

    // ---- Create -------------------------------------------------------------

    public async Task<IMasterDataItemDto> CreateAsync(MasterDataCollectionKey key, UpsertMasterDataRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var result = key == MasterDataCollectionKey.Categories
            ? (IMasterDataItemDto)await CreateCategoryAsync(request, actorUserId, ct)
            : await CreateLookupAsync(key, request, actorUserId, ct);

        await InvalidateAggregateCacheAsync(ct);
        return result;
    }

    private async Task<CategoryDto> CreateCategoryAsync(UpsertMasterDataRequest request, Guid actorUserId, CancellationToken ct)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppValidationException("name", "Category name is required.");
        }

        var existing = await _db.Categories.ToListAsync(ct);
        if (existing.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AppValidationException("name", $"A category named '{name}' already exists.");
        }

        var nextSortOrder = existing.Count == 0 ? 1 : existing.Max(c => c.SortOrder) + 1;
        var category = new Category { Id = Guid.NewGuid(), Name = name, IsActive = true, SortOrder = nextSortOrder };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "MasterDataCreated", "Category", category.Id.ToString(), new { category.Name }, ct);
        return ToDto(category);
    }

    private Task<LookupItemDto> CreateLookupAsync(MasterDataCollectionKey key, UpsertMasterDataRequest request, Guid actorUserId, CancellationToken ct)
    {
        var code = (request.Code ?? string.Empty).Trim();
        var label = (request.Label ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new AppValidationException("code", "Code is required.");
        }
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new AppValidationException("label", "Label is required.");
        }

        return key switch
        {
            MasterDataCollectionKey.ServiceTypes => CreateLookupEntityAsync(_db.ServiceTypes, "ServiceType", code, label, actorUserId, ct),
            MasterDataCollectionKey.LeadStatuses => CreateLookupEntityAsync(_db.LeadStatuses, "LeadStatus", code, label, actorUserId, ct),
            MasterDataCollectionKey.ShipmentStatuses => CreateLookupEntityAsync(_db.ShipmentStatuses, "ShipmentStatus", code, label, actorUserId, ct),
            MasterDataCollectionKey.InvoiceStatuses => CreateLookupEntityAsync(_db.InvoiceStatuses, "InvoiceStatus", code, label, actorUserId, ct),
            MasterDataCollectionKey.VendorStatuses => CreateLookupEntityAsync(_db.VendorStatuses, "VendorStatus", code, label, actorUserId, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown master-data collection.")
        };
    }

    private async Task<LookupItemDto> CreateLookupEntityAsync<TEntity>(DbSet<TEntity> set, string entityType, string code, string label, Guid actorUserId, CancellationToken ct)
        where TEntity : class, ILookupEntity, new()
    {
        var existing = await set.ToListAsync(ct);
        if (existing.Any(e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AppValidationException("code", $"'{code}' already exists in this collection.");
        }

        var nextSortOrder = existing.Count == 0 ? 1 : existing.Max(e => e.SortOrder) + 1;
        var entity = new TEntity { Id = Guid.NewGuid(), Code = code, Label = label, IsActive = true, SortOrder = nextSortOrder };
        set.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "MasterDataCreated", entityType, entity.Id.ToString(), new { entity.Code, entity.Label }, ct);
        return ToDto(entity);
    }

    // ---- Update (rename / edit label) ----------------------------------------

    public async Task<IMasterDataItemDto?> UpdateAsync(MasterDataCollectionKey key, Guid id, UpsertMasterDataRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        IMasterDataItemDto? result = key == MasterDataCollectionKey.Categories
            ? await UpdateCategoryAsync(id, request, actorUserId, ct)
            : await UpdateLookupAsync(key, id, request, actorUserId, ct);

        if (result is not null)
        {
            await InvalidateAggregateCacheAsync(ct);
        }
        return result;
    }

    private async Task<CategoryDto?> UpdateCategoryAsync(Guid id, UpsertMasterDataRequest request, Guid actorUserId, CancellationToken ct)
    {
        var category = await _db.Categories.FindAsync([id], ct);
        if (category is null)
        {
            return null;
        }

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppValidationException("name", "Category name is required.");
        }

        var others = await _db.Categories.Where(c => c.Id != id).ToListAsync(ct);
        if (others.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AppValidationException("name", $"A category named '{name}' already exists.");
        }

        category.Name = name;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, "MasterDataUpdated", "Category", category.Id.ToString(), new { category.Name }, ct);
        return ToDto(category);
    }

    private Task<LookupItemDto?> UpdateLookupAsync(MasterDataCollectionKey key, Guid id, UpsertMasterDataRequest request, Guid actorUserId, CancellationToken ct)
    {
        var label = (request.Label ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new AppValidationException("label", "Label is required.");
        }

        return key switch
        {
            MasterDataCollectionKey.ServiceTypes => UpdateLookupEntityAsync(_db.ServiceTypes, "ServiceType", id, label, actorUserId, ct),
            MasterDataCollectionKey.LeadStatuses => UpdateLookupEntityAsync(_db.LeadStatuses, "LeadStatus", id, label, actorUserId, ct),
            MasterDataCollectionKey.ShipmentStatuses => UpdateLookupEntityAsync(_db.ShipmentStatuses, "ShipmentStatus", id, label, actorUserId, ct),
            MasterDataCollectionKey.InvoiceStatuses => UpdateLookupEntityAsync(_db.InvoiceStatuses, "InvoiceStatus", id, label, actorUserId, ct),
            MasterDataCollectionKey.VendorStatuses => UpdateLookupEntityAsync(_db.VendorStatuses, "VendorStatus", id, label, actorUserId, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown master-data collection.")
        };
    }

    private async Task<LookupItemDto?> UpdateLookupEntityAsync<TEntity>(DbSet<TEntity> set, string entityType, Guid id, string label, Guid actorUserId, CancellationToken ct)
        where TEntity : class, ILookupEntity, new()
    {
        var entity = await set.FindAsync([id], ct);
        if (entity is null)
        {
            return null;
        }

        entity.Label = label;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, "MasterDataUpdated", entityType, entity.Id.ToString(), new { entity.Code, entity.Label }, ct);
        return ToDto(entity);
    }

    // ---- Retire / Restore -----------------------------------------------------

    public Task<IMasterDataItemDto?> RetireAsync(MasterDataCollectionKey key, Guid id, Guid actorUserId, CancellationToken ct = default) =>
        SetActiveAsync(key, id, isActive: false, actorUserId, ct);

    public Task<IMasterDataItemDto?> RestoreAsync(MasterDataCollectionKey key, Guid id, Guid actorUserId, CancellationToken ct = default) =>
        SetActiveAsync(key, id, isActive: true, actorUserId, ct);

    private async Task<IMasterDataItemDto?> SetActiveAsync(MasterDataCollectionKey key, Guid id, bool isActive, Guid actorUserId, CancellationToken ct)
    {
        var action = isActive ? "MasterDataRestored" : "MasterDataRetired";
        IMasterDataItemDto? result;

        if (key == MasterDataCollectionKey.Categories)
        {
            var category = await _db.Categories.FindAsync([id], ct);
            if (category is null)
            {
                return null;
            }

            category.IsActive = isActive;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(actorUserId, action, "Category", category.Id.ToString(), null, ct);
            result = ToDto(category);
        }
        else
        {
            result = key switch
            {
                MasterDataCollectionKey.ServiceTypes => await SetLookupActiveAsync(_db.ServiceTypes, "ServiceType", id, isActive, action, actorUserId, ct),
                MasterDataCollectionKey.LeadStatuses => await SetLookupActiveAsync(_db.LeadStatuses, "LeadStatus", id, isActive, action, actorUserId, ct),
                MasterDataCollectionKey.ShipmentStatuses => await SetLookupActiveAsync(_db.ShipmentStatuses, "ShipmentStatus", id, isActive, action, actorUserId, ct),
                MasterDataCollectionKey.InvoiceStatuses => await SetLookupActiveAsync(_db.InvoiceStatuses, "InvoiceStatus", id, isActive, action, actorUserId, ct),
                MasterDataCollectionKey.VendorStatuses => await SetLookupActiveAsync(_db.VendorStatuses, "VendorStatus", id, isActive, action, actorUserId, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown master-data collection.")
            };
        }

        if (result is not null)
        {
            await InvalidateAggregateCacheAsync(ct);
        }
        return result;
    }

    private async Task<LookupItemDto?> SetLookupActiveAsync<TEntity>(DbSet<TEntity> set, string entityType, Guid id, bool isActive, string action, Guid actorUserId, CancellationToken ct)
        where TEntity : class, ILookupEntity, new()
    {
        var entity = await set.FindAsync([id], ct);
        if (entity is null)
        {
            return null;
        }

        entity.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, action, entityType, entity.Id.ToString(), null, ct);
        return ToDto(entity);
    }

    // ---- Reorder ----------------------------------------------------------------

    public async Task<IReadOnlyList<IMasterDataItemDto>> ReorderAsync(MasterDataCollectionKey key, IReadOnlyList<ReorderItemDto> items, Guid actorUserId, CancellationToken ct = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new AppValidationException("items", "At least one item is required to reorder.");
        }

        List<IMasterDataItemDto> result;

        if (key == MasterDataCollectionKey.Categories)
        {
            var all = await _db.Categories.ToListAsync(ct);
            var byId = all.ToDictionary(c => c.Id);
            EnsureAllIdsExist(items, byId.Keys, "category");

            foreach (var item in items)
            {
                byId[item.Id].SortOrder = item.SortOrder;
            }

            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(actorUserId, "MasterDataReordered", "Category", null, new { items }, ct);
            result = all.OrderBy(c => c.SortOrder).Select(c => (IMasterDataItemDto)ToDto(c)).ToList();
        }
        else
        {
            result = key switch
            {
                MasterDataCollectionKey.ServiceTypes => await ReorderLookupAsync(_db.ServiceTypes, "ServiceType", items, actorUserId, ct),
                MasterDataCollectionKey.LeadStatuses => await ReorderLookupAsync(_db.LeadStatuses, "LeadStatus", items, actorUserId, ct),
                MasterDataCollectionKey.ShipmentStatuses => await ReorderLookupAsync(_db.ShipmentStatuses, "ShipmentStatus", items, actorUserId, ct),
                MasterDataCollectionKey.InvoiceStatuses => await ReorderLookupAsync(_db.InvoiceStatuses, "InvoiceStatus", items, actorUserId, ct),
                MasterDataCollectionKey.VendorStatuses => await ReorderLookupAsync(_db.VendorStatuses, "VendorStatus", items, actorUserId, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown master-data collection.")
            };
        }

        await InvalidateAggregateCacheAsync(ct);
        return result;
    }

    private async Task<List<IMasterDataItemDto>> ReorderLookupAsync<TEntity>(DbSet<TEntity> set, string entityType, IReadOnlyList<ReorderItemDto> items, Guid actorUserId, CancellationToken ct)
        where TEntity : class, ILookupEntity, new()
    {
        var all = await set.ToListAsync(ct);
        var byId = all.ToDictionary(e => e.Id);
        EnsureAllIdsExist(items, byId.Keys, entityType);

        foreach (var item in items)
        {
            byId[item.Id].SortOrder = item.SortOrder;
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, "MasterDataReordered", entityType, null, new { items }, ct);
        return all.OrderBy(e => e.SortOrder).Select(e => (IMasterDataItemDto)ToDto(e)).ToList();
    }

    private static void EnsureAllIdsExist(IReadOnlyList<ReorderItemDto> items, IEnumerable<Guid> validIds, string entityLabel)
    {
        var validSet = validIds.ToHashSet();
        var unknown = items.Select(i => i.Id).Where(id => !validSet.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new AppValidationException("items", $"Unknown {entityLabel} id(s): {string.Join(", ", unknown)}.");
        }
    }

    // ---- Delete (E3-08: referential safety) --------------------------------------

    public async Task<MasterDataDeleteResult> DeleteAsync(MasterDataCollectionKey key, Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var result = key switch
        {
            MasterDataCollectionKey.Categories => await DeleteCategoryAsync(id, actorUserId, ct),
            MasterDataCollectionKey.ServiceTypes => await DeleteLookupAsync(_db.ServiceTypes, "ServiceType", id, IsServiceTypeReferencedAsync, actorUserId, ct),
            MasterDataCollectionKey.LeadStatuses => await DeleteLookupAsync(_db.LeadStatuses, "LeadStatus", id, IsLeadStatusReferencedAsync, actorUserId, ct),
            MasterDataCollectionKey.ShipmentStatuses => await DeleteLookupAsync(_db.ShipmentStatuses, "ShipmentStatus", id, IsShipmentStatusReferencedAsync, actorUserId, ct),
            MasterDataCollectionKey.InvoiceStatuses => await DeleteLookupAsync(_db.InvoiceStatuses, "InvoiceStatus", id, IsInvoiceStatusReferencedAsync, actorUserId, ct),
            MasterDataCollectionKey.VendorStatuses => await DeleteLookupAsync(_db.VendorStatuses, "VendorStatus", id, IsVendorStatusReferencedAsync, actorUserId, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown master-data collection.")
        };

        if (result.Deleted)
        {
            await InvalidateAggregateCacheAsync(ct);
        }
        return result;
    }

    private async Task<MasterDataDeleteResult> DeleteCategoryAsync(Guid id, Guid actorUserId, CancellationToken ct)
    {
        var category = await _db.Categories.FindAsync([id], ct);
        if (category is null)
        {
            return MasterDataDeleteResult.NotFoundResult();
        }

        if (await IsCategoryReferencedAsync(id, ct))
        {
            return MasterDataDeleteResult.Conflict("This category is still referenced by existing records. Retire it instead of deleting.");
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, "MasterDataDeleted", "Category", id.ToString(), new { category.Name }, ct);
        return MasterDataDeleteResult.Success();
    }

    private async Task<MasterDataDeleteResult> DeleteLookupAsync<TEntity>(
        DbSet<TEntity> set, string entityType, Guid id, Func<Guid, CancellationToken, Task<bool>> isReferenced, Guid actorUserId, CancellationToken ct)
        where TEntity : class, ILookupEntity, new()
    {
        var entity = await set.FindAsync([id], ct);
        if (entity is null)
        {
            return MasterDataDeleteResult.NotFoundResult();
        }

        if (await isReferenced(id, ct))
        {
            return MasterDataDeleteResult.Conflict($"This {entityType} is still referenced by existing records. Retire it instead of deleting.");
        }

        set.Remove(entity);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, "MasterDataDeleted", entityType, id.ToString(), new { entity.Code, entity.Label }, ct);
        return MasterDataDeleteResult.Success();
    }

    // ---- Referential-integrity checks (E3-08) --------------------------------------
    // One method per collection, each naming the concrete entities that FK into it
    // (TECH_SPEC §6). Sequential AnyAsync calls rather than one combined query — these
    // run only on a delete attempt (rare) and stay trivially readable/correct.

    private async Task<bool> IsCategoryReferencedAsync(Guid id, CancellationToken ct)
    {
        if (await _db.CustomerCategories.AnyAsync(x => x.CategoryId == id, ct)) return true;
        if (await _db.VendorCategories.AnyAsync(x => x.CategoryId == id, ct)) return true;
        if (await _db.CatalogSections.AnyAsync(x => x.CategoryId == id, ct)) return true;
        if (await _db.InventoryItems.AnyAsync(x => x.CategoryId == id, ct)) return true;
        return false;
    }

    private async Task<bool> IsServiceTypeReferencedAsync(Guid id, CancellationToken ct)
    {
        if (await _db.Customers.AnyAsync(x => x.ServiceTypeId == id, ct)) return true;
        if (await _db.Shipments.AnyAsync(x => x.ServiceTypeId == id, ct)) return true;
        return false;
    }

    private Task<bool> IsLeadStatusReferencedAsync(Guid id, CancellationToken ct) => _db.Customers.AnyAsync(x => x.StatusId == id, ct);
    private Task<bool> IsShipmentStatusReferencedAsync(Guid id, CancellationToken ct) => _db.Shipments.AnyAsync(x => x.StatusId == id, ct);
    private Task<bool> IsInvoiceStatusReferencedAsync(Guid id, CancellationToken ct) => _db.Invoices.AnyAsync(x => x.StatusId == id, ct);
    private Task<bool> IsVendorStatusReferencedAsync(Guid id, CancellationToken ct) => _db.Vendors.AnyAsync(x => x.StatusId == id, ct);

    private async Task InvalidateAggregateCacheAsync(CancellationToken ct)
    {
        await _cache.RemoveAsync(AggregateActiveCacheKey, ct);
        await _cache.RemoveAsync(AggregateAllCacheKey, ct);
    }
}
