using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Inventory;

/// <summary>
/// Implements ACTION_PLAN E7-01…E7-04. Mirrors <c>VendorService</c>'s structure exactly: list
/// queries materialize the current page (with <c>Include</c>) and map client-side rather than
/// projecting inside the LINQ expression tree, so the same query shape is correct against both
/// the real Npgsql provider and the EF Core InMemory provider the Application-layer unit tests
/// use. Create/Update load the full <see cref="Category"/>/<see cref="Vendor"/> entities rather
/// than only validating that the ids exist, so the freshly-mapped response carries real
/// Name text without a second round trip — same reason VendorService does.
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;

    public InventoryService(IAppDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    // ---- List / search / filter + summary (E7-03, E7-04, D-k) ------------------------

    public async Task<InventoryListResultDto> ListAsync(InventoryListQuery query, CancellationToken ct = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 200 ? 25 : query.PageSize;

        if (!StockLevelFilters.IsValid(query.StockLevel))
        {
            throw new AppValidationException("stockLevel", $"Unknown stock level filter '{query.StockLevel}'. Expected 'all', 'low' or 'healthy'.");
        }

        var filtered = _db.InventoryItems.AsQueryable();

        if (query.CategoryId.HasValue)
        {
            filtered = filtered.Where(i => i.CategoryId == query.CategoryId.Value);
        }
        if (query.VendorId.HasValue)
        {
            filtered = filtered.Where(i => i.VendorId == query.VendorId.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // .ToLower().Contains(...) rather than EF.Functions.ILike — must translate
            // identically against both Npgsql and the EF Core InMemory provider, exactly as
            // VendorService.ListAsync and CustomerService.ListAsync already do.
            var term = query.Search.Trim().ToLowerInvariant();
            filtered = filtered.Where(i =>
                i.Name.ToLower().Contains(term) ||
                (i.Sku != null && i.Sku.ToLower().Contains(term)));
        }

        // "low" is below-reorder OR negative in one option, matching the prototype's single
        // "Low or negative" entry and its `i.qty < i.reorder` predicate — a negative quantity
        // is always below any non-negative threshold, so the one comparison covers both.
        if (!string.IsNullOrWhiteSpace(query.StockLevel))
        {
            if (query.StockLevel.Equals(StockLevelFilters.Low, StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(i => i.OnHandQty < i.ReorderThreshold);
            }
            else if (query.StockLevel.Equals(StockLevelFilters.Healthy, StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(i => i.OnHandQty >= i.ReorderThreshold);
            }
        }

        var totalCount = await filtered.CountAsync(ct);

        // D-k: the summary is computed over the WHOLE filtered set, not the page. Aggregated
        // in the database rather than over `pageEntities`, or paging would silently change the
        // stat tiles. Only the three columns the tiles need are pulled back, so this stays a
        // narrow projection even at volume.
        var summarySource = await filtered
            .Select(i => new { i.OnHandQty, i.ReorderThreshold, i.UnitCost })
            .ToListAsync(ct);

        var summary = new InventorySummaryDto(
            OnHandValue: summarySource.Sum(i => i.OnHandQty * (i.UnitCost ?? 0m)),
            ItemCount: summarySource.Count,
            LowStockCount: summarySource.Count(i => i.OnHandQty >= 0 && i.OnHandQty < i.ReorderThreshold),
            NegativeStockCount: summarySource.Count(i => i.OnHandQty < 0));

        var pageEntities = await filtered
            .Include(i => i.Category)
            .Include(i => i.Vendor)
            .OrderBy(i => i.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = pageEntities.Select(MapItem).ToList();
        return new InventoryListResultDto(items, page, pageSize, totalCount, summary);
    }

    // ---- Get one (E7-01) -------------------------------------------------------------

    public async Task<InventoryItemDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var item = await LoadWithNavigationsAsync(id, ct);
        return item is null ? null : MapItem(item);
    }

    // ---- Create (E7-01) --------------------------------------------------------------

    public async Task<InventoryItemDto> CreateAsync(CreateInventoryItemRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppValidationException("name", "Name is required.");
        }

        var category = await ResolveCategoryAsync(request.CategoryId, ct);
        var vendor = await ResolveVendorAsync(request.VendorId, ct);

        var reorderThreshold = request.ReorderThreshold ?? 0m;
        if (reorderThreshold < 0)
        {
            throw new AppValidationException("reorderThreshold", "Reorder threshold cannot be negative.");
        }
        if (request.UnitCost is < 0)
        {
            throw new AppValidationException("unitCost", "Unit cost cannot be negative.");
        }

        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            Name = name,
            Sku = Trim(request.Sku),
            Description = Trim(request.Description),
            CategoryId = category.Id,
            Category = category,
            VendorId = vendor?.Id,
            Vendor = vendor,
            Unit = Trim(request.Unit) ?? "pcs",
            // Opening balance only — every later movement goes through RecordInboundAsync or a
            // shipment line, which is why UpdateInventoryItemRequest has no OnHandQty at all.
            OnHandQty = request.OnHandQty ?? 0m,
            ReorderThreshold = reorderThreshold,
            UnitCost = request.UnitCost
        };

        _db.InventoryItems.Add(item);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "InventoryItemCreated", "InventoryItem", item.Id.ToString(),
            new { item.Name, item.Sku, item.OnHandQty, item.ReorderThreshold, CategoryName = category.Name }, ct);

        return MapItem(item);
    }

    // ---- Update (E7-01) --------------------------------------------------------------

    public async Task<InventoryItemDto?> UpdateAsync(Guid id, UpdateInventoryItemRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var item = await LoadWithNavigationsAsync(id, ct);
        if (item is null)
        {
            return null;
        }

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppValidationException("name", "Name is required.");
        }

        var category = await ResolveCategoryAsync(request.CategoryId, ct);
        var vendor = await ResolveVendorAsync(request.VendorId, ct);

        var reorderThreshold = request.ReorderThreshold ?? 0m;
        if (reorderThreshold < 0)
        {
            throw new AppValidationException("reorderThreshold", "Reorder threshold cannot be negative.");
        }
        if (request.UnitCost is < 0)
        {
            throw new AppValidationException("unitCost", "Unit cost cannot be negative.");
        }

        item.Name = name;
        item.Sku = Trim(request.Sku);
        item.Description = Trim(request.Description);
        item.CategoryId = category.Id;
        item.Category = category;
        item.VendorId = vendor?.Id;
        item.Vendor = vendor;
        item.Unit = Trim(request.Unit) ?? "pcs";
        item.ReorderThreshold = reorderThreshold;
        item.UnitCost = request.UnitCost;
        // OnHandQty is deliberately untouched — see UpdateInventoryItemRequest's doc comment.

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "InventoryItemUpdated", "InventoryItem", item.Id.ToString(),
            new { item.Name, item.Sku, item.ReorderThreshold, CategoryName = category.Name }, ct);

        return MapItem(item);
    }

    // ---- Delete (E7-01) --------------------------------------------------------------

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return false;
        }

        // ShipmentLine.InventoryItem is DeleteBehavior.Restrict, so the database would reject
        // this anyway — checked here so the caller gets an actionable 400 ProblemDetails
        // instead of a 500 from a raw FK violation. Same intent as E3-08's referential-safety
        // check on master data.
        if (await _db.ShipmentLines.AnyAsync(l => l.InventoryItemId == id, ct))
        {
            throw new AppValidationException("id", "This item is referenced by one or more shipments and cannot be deleted.");
        }

        _db.InventoryItems.Remove(item);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "InventoryItemDeleted", "InventoryItem", id.ToString(),
            new { item.Name, item.Sku }, ct);
        return true;
    }

    // ---- Record inbound stock (E7-02, D-d) -------------------------------------------

    public async Task<RecordInboundResultDto?> RecordInboundAsync(Guid id, RecordInboundRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var item = await LoadWithNavigationsAsync(id, ct);
        if (item is null)
        {
            return null;
        }

        if (request.Quantity <= 0)
        {
            throw new AppValidationException("quantity", "Inbound quantity must be greater than zero.");
        }

        var entry = new InventoryInboundEntry
        {
            Id = Guid.NewGuid(),
            InventoryItemId = item.Id,
            Quantity = request.Quantity,
            EntryDate = request.EntryDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            Reference = Trim(request.Reference),
            RecordedByUserId = actorUserId,
            CreatedAt = DateTime.UtcNow
        };

        item.OnHandQty += request.Quantity;
        _db.InventoryInboundEntries.Add(entry);

        // ONE SaveChangesAsync for both the entry insert and the on-hand increment. EF Core
        // wraps a single SaveChanges in a transaction, so E7-02's "increases on-hand quantity
        // atomically" holds without an explicit BeginTransaction — and an explicit one is not
        // available here anyway, since IAppDbContext deliberately exposes only SaveChangesAsync
        // (TECH_SPEC §4.1's narrow seam).
        await _db.SaveChangesAsync(ct);

        var recordedBy = await _db.Users.FindAsync([actorUserId], ct);

        await _audit.LogAsync(actorUserId, "InventoryInboundRecorded", "InventoryItem", item.Id.ToString(),
            new { entry.Quantity, EntryDate = entry.EntryDate.ToString("yyyy-MM-dd"), entry.Reference, NewOnHandQty = item.OnHandQty }, ct);

        return new RecordInboundResultDto(
            MapInboundEntry(entry, recordedBy?.Name ?? "(unknown)"),
            MapItem(item));
    }

    public async Task<InventoryInboundEntryListResultDto?> ListInboundEntriesAsync(Guid id, CancellationToken ct = default)
    {
        var exists = await _db.InventoryItems.AnyAsync(i => i.Id == id, ct);
        if (!exists)
        {
            return null;
        }

        var entries = await _db.InventoryInboundEntries
            .Where(e => e.InventoryItemId == id)
            .Include(e => e.RecordedBy)
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        return new InventoryInboundEntryListResultDto(
            entries.Select(e => MapInboundEntry(e, e.RecordedBy?.Name ?? "(unknown)")).ToList());
    }

    // ---- Shared helpers ---------------------------------------------------------------

    private Task<InventoryItem?> LoadWithNavigationsAsync(Guid id, CancellationToken ct) =>
        _db.InventoryItems
            .Include(i => i.Category)
            .Include(i => i.Vendor)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    private async Task<Category> ResolveCategoryAsync(Guid categoryId, CancellationToken ct) =>
        await _db.Categories.FindAsync([categoryId], ct)
            ?? throw new AppValidationException("categoryId", "Unknown category.");

    private async Task<Vendor?> ResolveVendorAsync(Guid? vendorId, CancellationToken ct)
    {
        if (!vendorId.HasValue)
        {
            return null;
        }

        return await _db.Vendors.FindAsync([vendorId.Value], ct)
            ?? throw new AppValidationException("vendorId", "Unknown vendor.");
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    internal static InventoryItemDto MapItem(InventoryItem i) => new(
        i.Id,
        i.Name,
        i.Sku,
        i.Description,
        new CategoryRefDto(i.CategoryId, i.Category?.Name ?? "(unknown)"),
        i.Vendor is null ? null : new VendorRefDto(i.Vendor.Id, i.Vendor.Name),
        i.Unit,
        i.OnHandQty,
        i.ReorderThreshold,
        i.UnitCost,
        // Null rather than 0 when the item is not costed, so the screen can tell
        // "worth nothing" apart from "no cost captured yet".
        i.UnitCost.HasValue ? i.OnHandQty * i.UnitCost.Value : null,
        StockLevels.For(i.OnHandQty, i.ReorderThreshold));

    private static InventoryInboundEntryDto MapInboundEntry(InventoryInboundEntry e, string recordedByName) => new(
        e.Id, e.InventoryItemId, e.Quantity, e.EntryDate, e.Reference,
        e.RecordedByUserId, recordedByName, AsUtc(e.CreatedAt));
}
