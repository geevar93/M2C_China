using SourcingOps.Application.Common;

namespace SourcingOps.Application.Vendors;

/// <summary>
/// ACTION_PLAN E5-07 / FR-VEN-07. Never exposes <see cref="Domain.Entities.VendorDocument.FilePath"/>
/// — the client only ever gets an id to hand to the authenticated download endpoint, matching
/// <c>CatalogDocumentDto</c>'s convention exactly. <see cref="DocType"/> is an embedded resolved
/// lookup object (<see cref="StatusRefDto"/>), not a bare id — the same convention this track
/// already uses for <c>Vendor.Status</c>/<c>CatalogSection.Category</c> (see RefDtos.cs). No
/// <c>IsLatest</c>/version-label fields: FSD Q5 scopes vendor documents to "filed for reference
/// only", explicitly without the version history <c>CatalogDocument</c> has for FR-CAT-03.
/// </summary>
public sealed record VendorDocumentDto(
    Guid Id,
    Guid VendorId,
    string OriginalFilename,
    long SizeBytes,
    StatusRefDto DocType,
    Guid UploadedByUserId,
    string UploadedByName,
    DateTime UploadedAt);

public sealed record VendorDocumentListResultDto(IReadOnlyList<VendorDocumentDto> Items);

/// <summary>Carries the open stream + metadata the authenticated download endpoint needs without exposing <c>FilePath</c> beyond the service boundary.</summary>
public sealed record VendorDocumentDownload(Stream Content, string OriginalFilename, string ContentType);
