using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Vendors;

/// <summary>
/// Implements ACTION_PLAN E5-07 / FR-VEN-07 — the vendor_documents table DR-4 flagged as
/// missing from TECH_SPEC §6. Mirrors <c>CatalogService</c>'s upload/download/delete shape
/// closely, deliberately without version history: FSD Q5 (TECH_SPEC §10 OI-8) scopes
/// vendor-level documents to "filed for reference only, no enforcement" — no <c>IsLatest</c>
/// demotion, and nothing anywhere reads <see cref="VendorDocument.DocTypeId"/> to gate or
/// block anything. A document type is still an FK into the <c>document_types</c> lookup
/// table (not free text/enum), per the DoD's FK rule and the D-2 precedent for VendorStatus.
/// </summary>
public sealed class VendorDocumentService : IVendorDocumentService
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IFileStorage _fileStorage;
    private readonly VendorUploadOptions _options;

    public VendorDocumentService(IAppDbContext db, IAuditLogger audit, IFileStorage fileStorage, VendorUploadOptions options)
    {
        _db = db;
        _audit = audit;
        _fileStorage = fileStorage;
        _options = options;
    }

    // ---- List (nested under a vendor) --------------------------------------------------

    public async Task<VendorDocumentListResultDto?> ListAsync(Guid vendorId, CancellationToken ct = default)
    {
        var vendorExists = await _db.Vendors.AnyAsync(v => v.Id == vendorId, ct);
        if (!vendorExists)
        {
            return null;
        }

        var documents = await _db.VendorDocuments
            .Where(d => d.VendorId == vendorId)
            .Include(d => d.DocType)
            .Include(d => d.UploadedBy)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync(ct);

        return new VendorDocumentListResultDto(documents.Select(MapDocument).ToList());
    }

    // ---- Upload -------------------------------------------------------------------------

    public async Task<VendorDocumentDto?> UploadDocumentAsync(
        Guid vendorId, Stream content, string originalFilename, string? contentType, long sizeBytes,
        Guid docTypeId, Guid actorUserId, CancellationToken ct = default)
    {
        var vendor = await _db.Vendors.FirstOrDefaultAsync(v => v.Id == vendorId, ct);
        if (vendor is null)
        {
            return null;
        }

        var docType = await _db.DocumentTypes.FindAsync([docTypeId], ct)
            ?? throw new AppValidationException("docTypeId", "Unknown document type.");

        var validation = await PdfUploadValidator.ValidateAsync(contentType, sizeBytes, content, _options.MaxUploadSizeBytes, ct);
        if (!validation.IsValid)
        {
            throw new AppValidationException("file", validation.Error!);
        }

        var documentId = Guid.NewGuid();
        var sanitizedName = FileNameSanitizer.Sanitize(originalFilename);
        var relativePath = $"vendor-docs/{vendorId}/{documentId}-{sanitizedName}";
        var storedPath = await _fileStorage.SaveAsync(relativePath, content, ct);

        var document = new VendorDocument
        {
            Id = documentId,
            VendorId = vendorId,
            FilePath = storedPath,
            OriginalFilename = sanitizedName,
            SizeBytes = sizeBytes,
            DocTypeId = docType.Id,
            DocType = docType,
            UploadedByUserId = actorUserId,
            UploadedAt = DateTime.UtcNow
        };
        _db.VendorDocuments.Add(document);

        await _db.SaveChangesAsync(ct);

        var uploader = await _db.Users.FindAsync([actorUserId], ct);

        await _audit.LogAsync(actorUserId, "VendorDocumentUploaded", "VendorDocument", document.Id.ToString(),
            new { document.OriginalFilename, document.SizeBytes, VendorId = vendorId, DocTypeCode = docType.Code }, ct);

        return new VendorDocumentDto(
            document.Id, document.VendorId, document.OriginalFilename, document.SizeBytes,
            new StatusRefDto(docType.Id, docType.Code, docType.Label),
            document.UploadedByUserId, uploader?.Name ?? "(unknown)", AsUtc(document.UploadedAt));
    }

    // ---- Download -------------------------------------------------------------------------

    public async Task<VendorDocumentDownload?> DownloadDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await _db.VendorDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(document.FilePath, ct);
        return new VendorDocumentDownload(stream, document.OriginalFilename, "application/pdf");
    }

    // ---- Delete ---------------------------------------------------------------------------

    public async Task<bool> DeleteDocumentAsync(Guid documentId, Guid actorUserId, CancellationToken ct = default)
    {
        var document = await _db.VendorDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return false;
        }

        // Same ordering as CatalogService.DeleteAsync: delete the stored file first — if this
        // throws, the DB row (and the only path a client can ever reach the file through)
        // still exists, which is safer than an orphaned row pointing at an already-deleted file.
        await _fileStorage.DeleteAsync(document.FilePath, ct);

        _db.VendorDocuments.Remove(document);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "VendorDocumentDeleted", "VendorDocument", documentId.ToString(),
            new { document.OriginalFilename, VendorId = document.VendorId }, ct);
        return true;
    }

    // ---- Shared helpers ---------------------------------------------------------------------

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static VendorDocumentDto MapDocument(VendorDocument d) => new(
        d.Id, d.VendorId, d.OriginalFilename, d.SizeBytes,
        new StatusRefDto(d.DocType.Id, d.DocType.Code, d.DocType.Label),
        d.UploadedByUserId, d.UploadedBy?.Name ?? "(unknown)", AsUtc(d.UploadedAt));
}
