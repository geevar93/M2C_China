using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Catalog;

/// <summary>
/// Implements ACTION_PLAN E6-01…E6-07. Mirrors <c>CustomerService</c>/<c>VendorService</c>'s
/// structure: list queries materialize the current page and map client-side rather than
/// projecting inside the LINQ expression tree.
/// </summary>
public sealed class CatalogService : ICatalogService
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IFileStorage _fileStorage;
    private readonly CatalogUploadOptions _options;

    public CatalogService(IAppDbContext db, IAuditLogger audit, IFileStorage fileStorage, CatalogUploadOptions options)
    {
        _db = db;
        _audit = audit;
        _fileStorage = fileStorage;
        _options = options;
    }

    // ---- List / search / filter (E6-05) ----------------------------------------------

    public async Task<CatalogSectionListResultDto> ListAsync(CatalogSectionListQuery query, CancellationToken ct = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 200 ? 25 : query.PageSize;

        var filtered = _db.CatalogSections.AsQueryable();

        if (query.VendorId.HasValue)
        {
            filtered = filtered.Where(s => s.VendorId == query.VendorId.Value);
        }
        if (query.CategoryId.HasValue)
        {
            filtered = filtered.Where(s => s.CategoryId == query.CategoryId.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            filtered = filtered.Where(s => s.Tags.Contains(query.Tag));
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // .ToLower().Contains(...) — must translate identically against both the real
            // Npgsql provider and the EF Core InMemory provider used by the unit tests (same
            // reasoning as CustomerService/VendorService).
            var term = query.Search.Trim().ToLowerInvariant();
            filtered = filtered.Where(s => s.Title.ToLower().Contains(term));
        }

        var totalCount = await filtered.CountAsync(ct);

        var pageEntities = await filtered
            .Include(s => s.Vendor)
            .Include(s => s.Category)
            .Include(s => s.Documents).ThenInclude(d => d.UploadedBy)
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = pageEntities.Select(MapSection).ToList();
        return new CatalogSectionListResultDto(items, page, pageSize, totalCount);
    }

    // ---- Get one -----------------------------------------------------------------------

    public async Task<CatalogSectionDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var section = await LoadWithNavigationsAsync(id, ct);
        return section is null ? null : MapSection(section);
    }

    // ---- Create (E6-01) -----------------------------------------------------------------

    public async Task<CatalogSectionDto> CreateAsync(CreateCatalogSectionRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new AppValidationException("title", "Title is required.");
        }

        var vendor = await _db.Vendors.FindAsync([request.VendorId], ct)
            ?? throw new AppValidationException("vendorId", "Unknown vendor.");
        var category = await _db.Categories.FindAsync([request.CategoryId], ct)
            ?? throw new AppValidationException("categoryId", "Unknown category.");

        var section = new CatalogSection
        {
            Id = Guid.NewGuid(),
            VendorId = vendor.Id,
            Vendor = vendor,
            Title = title,
            CategoryId = category.Id,
            Category = category,
            Tags = NormalizeTags(request.Tags),
            CreatedAt = DateTime.UtcNow
        };

        _db.CatalogSections.Add(section);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "CatalogSectionCreated", "CatalogSection", section.Id.ToString(),
            new { section.Title, VendorName = vendor.Name }, ct);

        return MapSection(section);
    }

    // ---- Update ---------------------------------------------------------------------------

    public async Task<CatalogSectionDto?> UpdateAsync(Guid id, UpdateCatalogSectionRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var section = await LoadWithNavigationsAsync(id, ct);
        if (section is null)
        {
            return null;
        }

        var title = (request.Title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new AppValidationException("title", "Title is required.");
        }

        var category = await _db.Categories.FindAsync([request.CategoryId], ct)
            ?? throw new AppValidationException("categoryId", "Unknown category.");

        section.Title = title;
        section.CategoryId = category.Id;
        section.Category = category;
        section.Tags = NormalizeTags(request.Tags);

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, "CatalogSectionUpdated", "CatalogSection", section.Id.ToString(), new { section.Title }, ct);

        return MapSection(section);
    }

    // ---- Delete -----------------------------------------------------------------------------

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var section = await _db.CatalogSections.Include(s => s.Documents).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (section is null)
        {
            return false;
        }

        // Delete the stored files first — if this throws, the DB row (and, per E6-04, the
        // only path a client can ever reach the file through) still exists, which is safer
        // than an orphaned row pointing at an already-deleted file.
        foreach (var document in section.Documents)
        {
            await _fileStorage.DeleteAsync(document.FilePath, ct);
        }

        _db.CatalogSections.Remove(section); // cascades to catalog_documents rows (VendorConfiguration/CatalogConfigurations: DeleteBehavior.Cascade)
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "CatalogSectionDeleted", "CatalogSection", id.ToString(), new { section.Title }, ct);
        return true;
    }

    // ---- Upload / versioning (E6-02, E6-03, E6-06) -------------------------------------------

    public async Task<CatalogDocumentDto?> UploadDocumentAsync(
        Guid catalogSectionId, Stream content, string originalFilename, string? contentType, long sizeBytes,
        string? versionLabel, Guid actorUserId, CancellationToken ct = default)
    {
        var section = await _db.CatalogSections.FirstOrDefaultAsync(s => s.Id == catalogSectionId, ct);
        if (section is null)
        {
            return null;
        }

        var validation = await PdfUploadValidator.ValidateAsync(contentType, sizeBytes, content, _options.MaxUploadSizeBytes, ct);
        if (!validation.IsValid)
        {
            throw new AppValidationException("file", validation.Error!);
        }

        var documentId = Guid.NewGuid();
        var sanitizedName = FileNameSanitizer.Sanitize(originalFilename);
        var relativePath = $"catalog-docs/{catalogSectionId}/{documentId}-{sanitizedName}";
        var storedPath = await _fileStorage.SaveAsync(relativePath, content, ct);

        // E6-03: demote whatever was previously latest, then add the new row — both go out
        // in the one SaveChangesAsync call below, so the demotion and the new latest flag
        // land in the same DB transaction.
        var previousLatest = await _db.CatalogDocuments
            .Where(d => d.CatalogSectionId == catalogSectionId && d.IsLatest)
            .ToListAsync(ct);
        foreach (var previous in previousLatest)
        {
            previous.IsLatest = false;
        }

        var document = new CatalogDocument
        {
            Id = documentId,
            CatalogSectionId = catalogSectionId,
            FilePath = storedPath,
            OriginalFilename = sanitizedName,
            SizeBytes = sizeBytes,
            VersionLabel = string.IsNullOrWhiteSpace(versionLabel) ? null : versionLabel.Trim(),
            IsLatest = true,
            UploadedByUserId = actorUserId,
            UploadedAt = DateTime.UtcNow
        };
        _db.CatalogDocuments.Add(document);

        await _db.SaveChangesAsync(ct);

        var uploader = await _db.Users.FindAsync([actorUserId], ct);

        await _audit.LogAsync(actorUserId, "CatalogDocumentUploaded", "CatalogDocument", document.Id.ToString(),
            new { document.OriginalFilename, document.SizeBytes, CatalogSectionId = catalogSectionId }, ct);

        return new CatalogDocumentDto(
            document.Id, document.CatalogSectionId, document.OriginalFilename, document.SizeBytes,
            document.VersionLabel, document.IsLatest, document.UploadedByUserId, uploader?.Name ?? "(unknown)",
            AsUtc(document.UploadedAt));
    }

    // ---- Download (E6-04) --------------------------------------------------------------------

    public async Task<CatalogDocumentDownload?> DownloadDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await _db.CatalogDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(document.FilePath, ct);
        return new CatalogDocumentDownload(stream, document.OriginalFilename, "application/pdf");
    }

    // ---- Shared helpers ------------------------------------------------------------------------

    private Task<CatalogSection?> LoadWithNavigationsAsync(Guid id, CancellationToken ct) =>
        _db.CatalogSections
            .Include(s => s.Vendor)
            .Include(s => s.Category)
            .Include(s => s.Documents).ThenInclude(d => d.UploadedBy)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    /// <summary>E6-07: trim / drop-empty / case-insensitive de-dup — matches CustomerService.NormalizeTags exactly.</summary>
    private static string[] NormalizeTags(IReadOnlyList<string>? tags) =>
        (tags ?? []).Select(t => t?.Trim() ?? string.Empty).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static CategoryRefDto MapCategory(Category c) => new(c.Id, c.Name);

    private static CatalogSectionDto MapSection(CatalogSection s) => new(
        s.Id, s.VendorId, s.Vendor?.Name ?? string.Empty, s.Title, MapCategory(s.Category),
        s.Tags.ToList(), AsUtc(s.CreatedAt),
        s.Documents.OrderByDescending(d => d.UploadedAt).Select(MapDocument).ToList());

    private static CatalogDocumentDto MapDocument(CatalogDocument d) => new(
        d.Id, d.CatalogSectionId, d.OriginalFilename, d.SizeBytes, d.VersionLabel, d.IsLatest,
        d.UploadedByUserId, d.UploadedBy?.Name ?? "(unknown)", AsUtc(d.UploadedAt));
}
