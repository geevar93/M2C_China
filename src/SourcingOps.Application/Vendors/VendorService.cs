using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Catalog;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Vendors;

/// <summary>
/// Implements ACTION_PLAN E5-01…E5-06. Mirrors <c>CustomerService</c>'s structure: list
/// queries materialize the current page (with `Include`) and map client-side rather than
/// projecting inside the LINQ expression tree, so the exact same query shape is correct
/// against both the real Npgsql provider and the EF Core InMemory provider used by the
/// Application-layer unit tests.
///
/// Unlike Customer*Dto, the coordinator's binding vendor/catalog contract embeds resolved
/// lookup objects (<see cref="StatusRefDto"/>/<see cref="CategoryRefDto"/>) rather than bare
/// ids — see those types' doc comment. That means Create/Update must load the full
/// <see cref="Category"/>/<see cref="Domain.Entities.VendorStatus"/> entities (not just
/// validate the id exists) so the freshly-mapped response has real Code/Label/Name text
/// without a second round-trip query.
/// </summary>
public sealed class VendorService : IVendorService
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IFileStorage _fileStorage;

    public VendorService(IAppDbContext db, IAuditLogger audit, IFileStorage fileStorage)
    {
        _db = db;
        _audit = audit;
        _fileStorage = fileStorage;
    }

    // ---- List / search / filter (E5-04) --------------------------------------------

    public async Task<VendorListResultDto> ListAsync(VendorListQuery query, CancellationToken ct = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 200 ? 25 : query.PageSize;

        var filtered = _db.Vendors.AsQueryable();

        if (query.CategoryId.HasValue)
        {
            filtered = filtered.Where(v => v.VendorCategories.Any(vc => vc.CategoryId == query.CategoryId.Value));
        }
        if (!string.IsNullOrWhiteSpace(query.Region))
        {
            filtered = filtered.Where(v => v.Region == query.Region);
        }
        if (query.StatusId.HasValue)
        {
            filtered = filtered.Where(v => v.StatusId == query.StatusId.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // .ToLower().Contains(...) rather than EF.Functions.ILike — must translate
            // identically against both the real Npgsql provider and the EF Core InMemory
            // provider used by the Application-layer unit tests (same reasoning as
            // CustomerService.ListAsync).
            var term = query.Search.Trim().ToLowerInvariant();
            filtered = filtered.Where(v =>
                v.Name.ToLower().Contains(term) ||
                (v.ContactPerson != null && v.ContactPerson.ToLower().Contains(term)) ||
                (v.Phone != null && v.Phone.ToLower().Contains(term)));
        }

        var totalCount = await filtered.CountAsync(ct);

        var pageEntities = await filtered
            .Include(v => v.Status)
            .Include(v => v.VendorCategories).ThenInclude(vc => vc.Category)
            .Include(v => v.CatalogSections)
            .OrderByDescending(v => v.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = pageEntities.Select(MapListItem).ToList();
        return new VendorListResultDto(items, page, pageSize, totalCount);
    }

    // ---- Get one (E5-05) -------------------------------------------------------------

    public async Task<VendorDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var vendor = await LoadWithNavigationsAsync(id, ct);
        return vendor is null ? null : MapDetail(vendor);
    }

    // ---- Create (E5-01…E5-03, E5-06) --------------------------------------------------

    public async Task<VendorDetailDto> CreateAsync(CreateVendorRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppValidationException("name", "Name is required.");
        }

        var status = await ResolveStatusAsync(request.StatusId, ct);
        var categories = await ResolveCategoriesAsync(request.CategoryIds, ct);

        var vendor = new Vendor
        {
            Id = Guid.NewGuid(),
            Name = name,
            ContactPerson = Trim(request.ContactPerson),
            Phone = Trim(request.Phone),
            Email = Trim(request.Email),
            Region = Trim(request.Region),
            StatusId = status.Id,
            Status = status,
            Moq = Trim(request.Moq),
            LeadTime = Trim(request.LeadTime),
            PaymentTerms = Trim(request.PaymentTerms),
            ReliabilityRating = request.ReliabilityRating,
            Notes = Trim(request.Notes),
            CreatedAt = DateTime.UtcNow
        };

        foreach (var category in categories)
        {
            vendor.VendorCategories.Add(new VendorCategory { VendorId = vendor.Id, CategoryId = category.Id, Category = category });
        }

        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "VendorCreated", "Vendor", vendor.Id.ToString(), new { vendor.Name, StatusCode = status.Code }, ct);

        return MapDetail(vendor);
    }

    // ---- Update -----------------------------------------------------------------------

    public async Task<VendorDetailDto?> UpdateAsync(Guid id, UpdateVendorRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var vendor = await LoadWithNavigationsAsync(id, ct);
        if (vendor is null)
        {
            return null;
        }

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppValidationException("name", "Name is required.");
        }

        var status = await ResolveStatusAsync(request.StatusId, ct);
        var categories = await ResolveCategoriesAsync(request.CategoryIds, ct);

        vendor.Name = name;
        vendor.ContactPerson = Trim(request.ContactPerson);
        vendor.Phone = Trim(request.Phone);
        vendor.Email = Trim(request.Email);
        vendor.Region = Trim(request.Region);
        vendor.StatusId = status.Id;
        vendor.Status = status;
        vendor.Moq = Trim(request.Moq);
        vendor.LeadTime = Trim(request.LeadTime);
        vendor.PaymentTerms = Trim(request.PaymentTerms);
        vendor.ReliabilityRating = request.ReliabilityRating;
        vendor.Notes = Trim(request.Notes);

        foreach (var link in vendor.VendorCategories.ToList())
        {
            _db.VendorCategories.Remove(link);
        }
        vendor.VendorCategories.Clear();
        foreach (var category in categories)
        {
            vendor.VendorCategories.Add(new VendorCategory { VendorId = vendor.Id, CategoryId = category.Id, Category = category });
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, "VendorUpdated", "Vendor", vendor.Id.ToString(), new { vendor.Name, StatusCode = status.Code }, ct);

        return MapDetail(vendor);
    }

    // ---- Delete -------------------------------------------------------------------------

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var vendor = await _db.Vendors
            .Include(v => v.CatalogSections).ThenInclude(s => s.Documents)
            .Include(v => v.VendorDocuments)
            .FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vendor is null)
        {
            return false;
        }

        // Same ordering as CatalogService.DeleteAsync: stored files first, so a storage failure
        // leaves the rows (the only path to the files) intact rather than orphaning them.
        foreach (var document in vendor.CatalogSections.SelectMany(s => s.Documents))
        {
            await _fileStorage.DeleteAsync(document.FilePath, ct);
        }
        foreach (var document in vendor.VendorDocuments)
        {
            await _fileStorage.DeleteAsync(document.FilePath, ct);
        }

        // Inventory items only reference the vendor optionally (SET NULL in the database);
        // clear them explicitly too so the behaviour doesn't depend on the provider.
        var linkedItems = await _db.InventoryItems.Where(i => i.VendorId == id).ToListAsync(ct);
        foreach (var item in linkedItems)
        {
            item.VendorId = null;
        }

        // Cascades to vendor_categories, catalog_sections → catalog_documents, vendor_documents.
        _db.Vendors.Remove(vendor);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "VendorDeleted", "Vendor", id.ToString(),
            new { vendor.Name, CatalogSections = vendor.CatalogSections.Count }, ct);
        return true;
    }

    // ---- Shared helpers -----------------------------------------------------------------

    private Task<Vendor?> LoadWithNavigationsAsync(Guid id, CancellationToken ct) =>
        _db.Vendors
            .Include(v => v.Status)
            .Include(v => v.VendorCategories).ThenInclude(vc => vc.Category)
            .Include(v => v.CatalogSections).ThenInclude(s => s.Category)
            .Include(v => v.CatalogSections).ThenInclude(s => s.Documents).ThenInclude(d => d.UploadedBy)
            .FirstOrDefaultAsync(v => v.Id == id, ct);

    private async Task<Domain.Entities.VendorStatus> ResolveStatusAsync(Guid statusId, CancellationToken ct) =>
        await _db.VendorStatuses.FindAsync([statusId], ct)
            ?? throw new AppValidationException("statusId", "Unknown vendor status.");

    private async Task<List<Category>> ResolveCategoriesAsync(IReadOnlyList<Guid>? requested, CancellationToken ct)
    {
        if (requested is null || requested.Count == 0)
        {
            return [];
        }

        var distinct = requested.Distinct().ToList();
        var existing = await _db.Categories.Where(c => distinct.Contains(c.Id)).ToListAsync(ct);
        if (existing.Count != distinct.Count)
        {
            var existingSet = existing.Select(c => c.Id).ToHashSet();
            var unknown = distinct.Where(catId => !existingSet.Contains(catId));
            throw new AppValidationException("categoryIds", $"Unknown category id(s): {string.Join(", ", unknown)}.");
        }

        return existing;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static StatusRefDto MapStatus(Domain.Entities.VendorStatus s) => new(s.Id, s.Code, s.Label);

    private static CategoryRefDto MapCategory(Category c) => new(c.Id, c.Name);

    private static VendorListItemDto MapListItem(Vendor v) => new(
        v.Id, v.Name, v.ContactPerson, v.Phone, v.Region,
        v.VendorCategories.Select(vc => MapCategory(vc.Category)).ToList(),
        MapStatus(v.Status),
        v.Moq, v.LeadTime, v.ReliabilityRating,
        v.CatalogSections.Count);

    private static VendorDetailDto MapDetail(Vendor v) => new(
        v.Id, v.Name, v.ContactPerson, v.Phone, v.Region,
        v.VendorCategories.Select(vc => MapCategory(vc.Category)).ToList(),
        MapStatus(v.Status),
        v.Moq, v.LeadTime, v.ReliabilityRating,
        v.CatalogSections.Count,
        v.Email, v.PaymentTerms, v.Notes, AsUtc(v.CreatedAt),
        v.CatalogSections.OrderByDescending(s => s.CreatedAt).Select(s => MapCatalogSection(s, v.Name)).ToList());

    // vendorName is passed explicitly (the aggregate root, `v`, is always already loaded here)
    // rather than reading `s.Vendor.Name` — this method is reached via
    // `LoadWithNavigationsAsync`, which never Includes CatalogSection.Vendor's back-reference,
    // so relying on it would be a silent null under the real Npgsql provider too (EF Core does
    // not fix up a reference navigation it was never asked to load).
    private static CatalogSectionDto MapCatalogSection(CatalogSection s, string vendorName) => new(
        s.Id, s.VendorId, vendorName, s.Title, MapCategory(s.Category),
        s.Tags.ToList(), AsUtc(s.CreatedAt),
        s.Documents.OrderByDescending(d => d.UploadedAt).Select(MapDocument).ToList());

    private static CatalogDocumentDto MapDocument(CatalogDocument d) => new(
        d.Id, d.CatalogSectionId, d.OriginalFilename, d.SizeBytes, d.VersionLabel, d.IsLatest,
        d.UploadedByUserId, d.UploadedBy?.Name ?? "(unknown)", AsUtc(d.UploadedAt));
}
