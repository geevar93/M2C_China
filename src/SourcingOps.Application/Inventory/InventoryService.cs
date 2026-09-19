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
    public const long MaxImageBytes = 8 * 1024 * 1024;
    public const int MaxThumbnailBytes = 200 * 1024;
    public const string CountEditedReason = "Count edited";

    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IFileStorage _fileStorage;

    public InventoryService(IAppDbContext db, IAuditLogger audit, IFileStorage fileStorage)
    {
        _db = db;
        _audit = audit;
        _fileStorage = fileStorage;
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
        if (request.SellingPrice is < 0)
        {
            throw new AppValidationException("sellingPrice", "Selling price cannot be negative.");
        }

        if (request.GstRate is < 0 or > 100)
        {
            throw new AppValidationException("gstRate", "GST rate must be between 0 and 100.");
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
            UnitCost = request.UnitCost,
            SellingPrice = request.SellingPrice,
            HsnCode = Trim(request.HsnCode),
            GstRate = request.GstRate
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
        if (request.SellingPrice is < 0)
        {
            throw new AppValidationException("sellingPrice", "Selling price cannot be negative.");
        }

        if (request.GstRate is < 0 or > 100)
        {
            throw new AppValidationException("gstRate", "GST rate must be between 0 and 100.");
        }

        if (request.UnitCost is < 0)
        {
            throw new AppValidationException("unitCost", "Unit cost cannot be negative.");
        }

        if (request.OnHandQty is < 0)
        {
            throw new AppValidationException("onHandQty", "Current stock cannot be negative.");
        }

        // An edited count is recorded as a stock adjustment in the same SaveChanges, so the
        // balance never changes without a durable previous → new record.
        InventoryStockAdjustment? adjustment = null;
        if (request.OnHandQty is { } newQty && newQty != item.OnHandQty)
        {
            adjustment = new InventoryStockAdjustment
            {
                Id = Guid.NewGuid(),
                InventoryItemId = item.Id,
                CountedQty = newQty,
                PreviousQty = item.OnHandQty,
                Delta = newQty - item.OnHandQty,
                Reason = CountEditedReason,
                AdjustedOn = DateOnly.FromDateTime(DateTime.UtcNow),
                AdjustedAt = DateTime.UtcNow,
                AdjustedByUserId = actorUserId
            };
            _db.InventoryStockAdjustments.Add(adjustment);
            item.OnHandQty = newQty;
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
        item.SellingPrice = request.SellingPrice;
        item.HsnCode = Trim(request.HsnCode);
        item.GstRate = request.GstRate;

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "InventoryItemUpdated", "InventoryItem", item.Id.ToString(),
            new { item.Name, item.Sku, item.ReorderThreshold, CategoryName = category.Name }, ct);
        if (adjustment is not null)
        {
            await _audit.LogAsync(actorUserId, "InventoryStockAdjustmentRecorded", "InventoryItem", item.Id.ToString(),
                new { adjustment.CountedQty, adjustment.PreviousQty, adjustment.Delta, adjustment.Reason, NewOnHandQty = item.OnHandQty }, ct);
        }

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

        if (item.ImagePath is not null)
        {
            await _fileStorage.DeleteAsync(item.ImagePath, ct);
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

    // ---- Record stock adjustment (N-38) -----------------------------------------------

    public async Task<RecordAdjustmentResultDto?> RecordAdjustmentAsync(Guid id, RecordAdjustmentRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var item = await LoadWithNavigationsAsync(id, ct);
        if (item is null)
        {
            return null;
        }

        if (request.CountedQty < 0)
        {
            throw new AppValidationException("countedQty", "Counted quantity cannot be negative.");
        }

        var reason = (request.Reason ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new AppValidationException("reason", "Reason is required.");
        }

        var previousQty = item.OnHandQty;

        var adjustment = new InventoryStockAdjustment
        {
            Id = Guid.NewGuid(),
            InventoryItemId = item.Id,
            CountedQty = request.CountedQty,
            PreviousQty = previousQty,
            // Stored, not recomputed later — see InventoryStockAdjustment's doc comment. May be
            // zero: "we counted and it was correct" is still a fact worth keeping.
            Delta = request.CountedQty - previousQty,
            Reason = reason,
            AdjustedOn = request.AdjustedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
            AdjustedAt = DateTime.UtcNow,
            AdjustedByUserId = actorUserId
        };

        item.OnHandQty = request.CountedQty;
        _db.InventoryStockAdjustments.Add(adjustment);

        // ONE SaveChangesAsync for both the adjustment insert and the on-hand overwrite —
        // same reasoning as RecordInboundAsync's single-transaction comment above.
        await _db.SaveChangesAsync(ct);

        var adjustedBy = await _db.Users.FindAsync([actorUserId], ct);

        await _audit.LogAsync(actorUserId, "InventoryStockAdjustmentRecorded", "InventoryItem", item.Id.ToString(),
            new { adjustment.CountedQty, adjustment.PreviousQty, adjustment.Delta, adjustment.Reason, AdjustedOn = adjustment.AdjustedOn.ToString("yyyy-MM-dd"), NewOnHandQty = item.OnHandQty }, ct);

        return new RecordAdjustmentResultDto(
            MapStockAdjustment(adjustment, adjustedBy?.Name ?? "(unknown)"),
            MapItem(item));
    }

    public async Task<InventoryStockAdjustmentListResultDto?> ListStockAdjustmentsAsync(Guid id, CancellationToken ct = default)
    {
        var exists = await _db.InventoryItems.AnyAsync(i => i.Id == id, ct);
        if (!exists)
        {
            return null;
        }

        var adjustments = await _db.InventoryStockAdjustments
            .Where(a => a.InventoryItemId == id)
            .Include(a => a.AdjustedByUser)
            .OrderByDescending(a => a.AdjustedOn)
            .ThenByDescending(a => a.AdjustedAt)
            .ToListAsync(ct);

        return new InventoryStockAdjustmentListResultDto(
            adjustments.Select(a => MapStockAdjustment(a, a.AdjustedByUser?.Name ?? "(unknown)")).ToList());
    }

    // ---- Image (full size in file storage, thumbnail inline) --------------------------

    public async Task<InventoryItemDto?> SetImageAsync(Guid id, Stream image, long imageSize, byte[] thumbnail, Guid actorUserId, CancellationToken ct = default)
    {
        var item = await LoadWithNavigationsAsync(id, ct);
        if (item is null)
        {
            return null;
        }

        if (imageSize <= 0 || imageSize > MaxImageBytes)
        {
            throw new AppValidationException("file", $"Image must be between 1 byte and {MaxImageBytes / (1024 * 1024)} MB.");
        }
        if (thumbnail.Length == 0 || thumbnail.Length > MaxThumbnailBytes)
        {
            throw new AppValidationException("thumbnail", $"Thumbnail must be between 1 byte and {MaxThumbnailBytes / 1024} KB.");
        }

        // Buffer the (size-capped) upload so its leading bytes can be sniffed before storing.
        using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, ct);
        var imageType = ImageUploadValidator.Sniff(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)))
            ?? throw new AppValidationException("file", "Only JPEG, PNG or WebP images are supported.");
        var thumbType = ImageUploadValidator.Sniff(thumbnail)
            ?? throw new AppValidationException("thumbnail", "Thumbnail must be a JPEG, PNG or WebP image.");

        buffer.Position = 0;
        var relativePath = $"inventory-images/{item.Id}/{Guid.NewGuid():N}{ImageUploadValidator.ExtensionFor(imageType)}";
        var storedPath = await _fileStorage.SaveAsync(relativePath, buffer, ct);

        var previousPath = item.ImagePath;
        item.ImagePath = storedPath;
        item.ImageContentType = imageType;
        item.ThumbnailData = thumbnail;
        item.ThumbnailContentType = thumbType;
        await _db.SaveChangesAsync(ct);

        // Old file removed only after the row points at the new one.
        if (previousPath is not null)
        {
            await _fileStorage.DeleteAsync(previousPath, ct);
        }

        await _audit.LogAsync(actorUserId, "InventoryImageSet", "InventoryItem", item.Id.ToString(),
            new { ImageBytes = imageSize, ThumbnailBytes = thumbnail.Length, ContentType = imageType }, ct);

        return MapItem(item);
    }

    public async Task<InventoryItemDto?> RemoveImageAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var item = await LoadWithNavigationsAsync(id, ct);
        if (item is null)
        {
            return null;
        }
        if (item.ImagePath is null && item.ThumbnailData is null)
        {
            return MapItem(item);
        }

        if (item.ImagePath is not null)
        {
            await _fileStorage.DeleteAsync(item.ImagePath, ct);
        }
        item.ImagePath = null;
        item.ImageContentType = null;
        item.ThumbnailData = null;
        item.ThumbnailContentType = null;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "InventoryImageRemoved", "InventoryItem", item.Id.ToString(), null, ct);
        return MapItem(item);
    }

    public async Task<InventoryImageDownload?> GetImageAsync(Guid id, CancellationToken ct = default)
    {
        var item = await _db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item?.ImagePath is null)
        {
            return null;
        }
        var stream = await _fileStorage.OpenReadAsync(item.ImagePath, ct);
        return new InventoryImageDownload(stream, item.ImageContentType ?? ImageUploadValidator.Jpeg);
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
        i.SellingPrice,
        i.HsnCode,
        i.GstRate,
        // Null rather than 0 when the item is not costed, so the screen can tell
        // "worth nothing" apart from "no cost captured yet".
        i.UnitCost.HasValue ? i.OnHandQty * i.UnitCost.Value : null,
        StockLevels.For(i.OnHandQty, i.ReorderThreshold),
        HasImage: i.ImagePath is not null,
        ThumbnailDataUrl: i.ThumbnailData is { Length: > 0 }
            ? $"data:{i.ThumbnailContentType ?? ImageUploadValidator.Jpeg};base64,{Convert.ToBase64String(i.ThumbnailData)}"
            : null);

    private static InventoryInboundEntryDto MapInboundEntry(InventoryInboundEntry e, string recordedByName) => new(
        e.Id, e.InventoryItemId, e.Quantity, e.EntryDate, e.Reference,
        e.RecordedByUserId, recordedByName, AsUtc(e.CreatedAt));

    private static InventoryStockAdjustmentDto MapStockAdjustment(InventoryStockAdjustment a, string adjustedByName) => new(
        a.Id, a.InventoryItemId, a.CountedQty, a.PreviousQty, a.Delta, a.Reason, a.AdjustedOn,
        AsUtc(a.AdjustedAt), a.AdjustedByUserId, adjustedByName);
}
