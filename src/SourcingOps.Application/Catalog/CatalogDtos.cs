using SourcingOps.Application.Common;

namespace SourcingOps.Application.Catalog;

/// <summary>
/// Binding cross-track contract fixed by the coordinator (ACTION_PLAN E6). One shape serves
/// both the cross-vendor browse list (<c>GET /catalog-sections</c>, E6-05) and a single
/// section's detail (<c>GET /catalog-sections/{id}</c>) — unlike Vendor, the contract does not
/// define a separate slim list-item shape for catalog sections, so <see cref="Documents"/> is
/// always populated.
/// </summary>
public sealed record CatalogSectionDto(
    Guid Id,
    Guid VendorId,
    string VendorName,
    string Title,
    CategoryRefDto Category,
    IReadOnlyList<string> Tags,
    DateTime CreatedAt,
    IReadOnlyList<CatalogDocumentDto> Documents);

/// <summary>Never exposes <see cref="Domain.Entities.CatalogDocument.FilePath"/> — the client only ever gets an id to hand to the authenticated download endpoint (E6-04).</summary>
public sealed record CatalogDocumentDto(
    Guid Id,
    Guid CatalogSectionId,
    string OriginalFilename,
    long SizeBytes,
    string? VersionLabel,
    bool IsLatest,
    Guid UploadedByUserId,
    string UploadedByName,
    DateTime UploadedAt);

public sealed record CatalogSectionListResultDto(IReadOnlyList<CatalogSectionDto> Items, int Page, int PageSize, int TotalCount);

public sealed record CreateCatalogSectionRequest(
    Guid VendorId,
    string Title,
    Guid CategoryId,
    IReadOnlyList<string>? Tags);

/// <summary>VendorId is immutable after creation — a section belongs to the vendor it was created under (E6-01).</summary>
public sealed record UpdateCatalogSectionRequest(
    string Title,
    Guid CategoryId,
    IReadOnlyList<string>? Tags);

/// <summary>All filters plus search/paging from the binding <c>GET /catalog-sections</c> contract (E6-05).</summary>
public sealed record CatalogSectionListQuery(
    string? Search,
    int Page,
    int PageSize,
    Guid? CategoryId,
    Guid? VendorId,
    string? Tag);

/// <summary>Result of a document upload (E6-02/E6-03): the new document plus the section it now belongs to, latest-first.</summary>
public sealed record UploadCatalogDocumentRequest(string? VersionLabel);

/// <summary>Carries the open stream + metadata an authenticated download endpoint needs (E6-04) without exposing <c>FilePath</c> beyond the service boundary.</summary>
public sealed record CatalogDocumentDownload(Stream Content, string OriginalFilename, string ContentType);
