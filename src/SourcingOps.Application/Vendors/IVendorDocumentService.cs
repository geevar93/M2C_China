namespace SourcingOps.Application.Vendors;

/// <summary>Implements ACTION_PLAN E5-07 behind <c>/api/v1/vendors/{id}/documents</c> and <c>/api/v1/vendor-documents/{id}</c>.</summary>
public interface IVendorDocumentService
{
    /// <summary>Null return means the vendor id was not found.</summary>
    Task<VendorDocumentListResultDto?> ListAsync(Guid vendorId, CancellationToken ct = default);

    /// <summary>
    /// Validates the upload via <see cref="Common.PdfUploadValidator"/> (throws
    /// <see cref="Common.AppValidationException"/> on a validation failure or an unknown
    /// <paramref name="docTypeId"/>) and stores it. Null return means the vendor id was not
    /// found.
    /// </summary>
    Task<VendorDocumentDto?> UploadDocumentAsync(
        Guid vendorId, Stream content, string originalFilename, string? contentType, long sizeBytes,
        Guid docTypeId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Null return means the document id was not found (never a public static path — caller must be authenticated+permission-checked at the controller).</summary>
    Task<VendorDocumentDownload?> DownloadDocumentAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>Deletes the document row and its stored file. Returns false if the document id was not found.</summary>
    Task<bool> DeleteDocumentAsync(Guid documentId, Guid actorUserId, CancellationToken ct = default);
}
