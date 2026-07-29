using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Common;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Shipments;

/// <summary>
/// Implements ACTION_PLAN E7-09 / FR-INV-08 — packing list / AWB / BL documents attached to a
/// shipment. Reuses the EXACT <see cref="IFileStorage"/> + <see cref="PdfUploadValidator"/>
/// machinery <c>VendorDocumentService</c> uses (magic bytes and size, not extension), and like
/// it never lets <c>FilePath</c> cross the service boundary: the client only ever receives an
/// id to hand to the authenticated download endpoint (TECH_SPEC §8).
///
/// The document type is an FK into <c>document_types</c> filtered to
/// <see cref="DocumentTypeScopes.Shipment"/> (deviation D-f), so the shipment upload dropdown
/// cannot offer "Business Licence" and the vendor one cannot offer "Packing List".
/// </summary>
public sealed class ShipmentDocumentService : IShipmentDocumentService
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IFileStorage _fileStorage;
    private readonly ShipmentUploadOptions _options;

    public ShipmentDocumentService(IAppDbContext db, IAuditLogger audit, IFileStorage fileStorage, ShipmentUploadOptions options)
    {
        _db = db;
        _audit = audit;
        _fileStorage = fileStorage;
        _options = options;
    }

    // ---- List (nested under a shipment) --------------------------------------------------

    public async Task<ShipmentDocumentListResultDto?> ListAsync(Guid shipmentId, CancellationToken ct = default)
    {
        var shipmentExists = await _db.Shipments.AnyAsync(s => s.Id == shipmentId, ct);
        if (!shipmentExists)
        {
            return null;
        }

        var documents = await _db.ShipmentDocuments
            .Where(d => d.ShipmentId == shipmentId)
            .Include(d => d.DocumentType)
            .Include(d => d.UploadedBy)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync(ct);

        return new ShipmentDocumentListResultDto(documents.Select(ShipmentService.MapDocument).ToList());
    }

    // ---- Upload ----------------------------------------------------------------------------

    public async Task<ShipmentDocumentDto?> UploadDocumentAsync(
        Guid shipmentId, Stream content, string originalFilename, string? contentType, long sizeBytes,
        Guid documentTypeId, Guid actorUserId, CancellationToken ct = default)
    {
        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId, ct);
        if (shipment is null)
        {
            return null;
        }

        var documentType = await _db.DocumentTypes.FindAsync([documentTypeId], ct)
            ?? throw new AppValidationException("documentTypeId", "Unknown document type.");

        // D-f: a vendor-scoped type on a shipment upload is rejected rather than quietly
        // accepted — the whole reason the scope column exists is that the two sets are not
        // interchangeable.
        if (!string.Equals(documentType.Scope, DocumentTypeScopes.Shipment, StringComparison.Ordinal))
        {
            throw new AppValidationException("documentTypeId", "This document type is not valid for shipment documents.");
        }

        var validation = await PdfUploadValidator.ValidateAsync(contentType, sizeBytes, content, _options.MaxUploadSizeBytes, ct);
        if (!validation.IsValid)
        {
            throw new AppValidationException("file", validation.Error!);
        }

        var documentId = Guid.NewGuid();
        var sanitizedName = FileNameSanitizer.Sanitize(originalFilename);
        // TECH_SPEC §4.6's stated convention: /uploads/shipment-docs/{shipmentId}/{filename}.
        // The document id is prefixed onto the filename exactly as the vendor and catalog paths
        // do, so two uploads of the same filename to one shipment cannot overwrite each other.
        var relativePath = $"shipment-docs/{shipmentId}/{documentId}-{sanitizedName}";
        var storedPath = await _fileStorage.SaveAsync(relativePath, content, ct);

        var document = new ShipmentDocument
        {
            Id = documentId,
            ShipmentId = shipmentId,
            FilePath = storedPath,
            OriginalFilename = sanitizedName,
            SizeBytes = sizeBytes,
            DocumentTypeId = documentType.Id,
            DocumentType = documentType,
            UploadedByUserId = actorUserId,
            UploadedAt = DateTime.UtcNow
        };
        _db.ShipmentDocuments.Add(document);

        await _db.SaveChangesAsync(ct);

        var uploader = await _db.Users.FindAsync([actorUserId], ct);

        await _audit.LogAsync(actorUserId, "ShipmentDocumentUploaded", "ShipmentDocument", document.Id.ToString(),
            new { document.OriginalFilename, document.SizeBytes, ShipmentId = shipmentId, DocumentTypeCode = documentType.Code }, ct);

        return new ShipmentDocumentDto(
            document.Id, document.ShipmentId, document.OriginalFilename, document.SizeBytes,
            new StatusRefDto(documentType.Id, documentType.Code, documentType.Label),
            document.UploadedByUserId, uploader?.Name ?? "(unknown)", DateTime.SpecifyKind(document.UploadedAt, DateTimeKind.Utc));
    }

    // ---- Download ----------------------------------------------------------------------------

    public async Task<ShipmentDocumentDownload?> DownloadDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await _db.ShipmentDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(document.FilePath, ct);
        return new ShipmentDocumentDownload(stream, document.OriginalFilename, "application/pdf");
    }

    // ---- Delete -------------------------------------------------------------------------------

    public async Task<bool> DeleteDocumentAsync(Guid documentId, Guid actorUserId, CancellationToken ct = default)
    {
        var document = await _db.ShipmentDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document is null)
        {
            return false;
        }

        // Same ordering as CatalogService/VendorDocumentService (D-29): delete the stored file
        // first — if this throws, the DB row (and the only path a client can reach the file
        // through) still exists, which is recoverable, whereas an orphaned row pointing at an
        // already-deleted file is not.
        await _fileStorage.DeleteAsync(document.FilePath, ct);

        _db.ShipmentDocuments.Remove(document);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "ShipmentDocumentDeleted", "ShipmentDocument", documentId.ToString(),
            new { document.OriginalFilename, ShipmentId = document.ShipmentId }, ct);
        return true;
    }
}
