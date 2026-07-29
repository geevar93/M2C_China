namespace SourcingOps.Application.Catalog;

/// <summary>Implements ACTION_PLAN E6-01…E6-07 behind the coordinator's binding <c>/api/v1/catalog-sections</c> / <c>/api/v1/catalog-documents</c> contract.</summary>
public interface ICatalogService
{
    Task<CatalogSectionListResultDto> ListAsync(CatalogSectionListQuery query, CancellationToken ct = default);

    /// <summary>Null return means the section id was not found.</summary>
    Task<CatalogSectionDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Throws <see cref="Common.AppValidationException"/> if <c>vendorId</c>/<c>categoryId</c> is unknown.</summary>
    Task<CatalogSectionDto> CreateAsync(CreateCatalogSectionRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Null return means the section id was not found.</summary>
    Task<CatalogSectionDto?> UpdateAsync(Guid id, UpdateCatalogSectionRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Deletes the section, its document rows, and their stored files. Returns false if the section id was not found.</summary>
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// E6-02/E6-03/E6-06: validates the upload via <see cref="Common.PdfUploadValidator"/> (throws
    /// <see cref="Common.AppValidationException"/> on failure), stores it, and — in the same
    /// <c>SaveChangesAsync</c> — demotes whatever was previously the section's latest document.
    /// Null return means the section id was not found.
    /// </summary>
    Task<CatalogDocumentDto?> UploadDocumentAsync(
        Guid catalogSectionId, Stream content, string originalFilename, string? contentType, long sizeBytes,
        string? versionLabel, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Null return means the document id was not found (E6-04 — never a public static path; caller must be authenticated+permission-checked at the controller).</summary>
    Task<CatalogDocumentDownload?> DownloadDocumentAsync(Guid documentId, CancellationToken ct = default);
}
