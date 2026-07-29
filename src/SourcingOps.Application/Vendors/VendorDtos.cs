using SourcingOps.Application.Catalog;
using SourcingOps.Application.Common;

namespace SourcingOps.Application.Vendors;

/// <summary>
/// Binding cross-track contract fixed by the coordinator (ACTION_PLAN E5). Unlike the CRM
/// track, <see cref="Status"/> and <see cref="Categories"/> are embedded resolved lookup
/// objects, not bare ids — see <see cref="StatusRefDto"/>/<see cref="CategoryRefDto"/>'s doc
/// comment for why this track deliberately differs from Customer*Dto's id-only convention.
/// </summary>
public sealed record VendorListItemDto(
    Guid Id,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Region,
    IReadOnlyList<CategoryRefDto> Categories,
    StatusRefDto Status,
    string? Moq,
    string? LeadTime,
    decimal? ReliabilityRating,
    int CatalogCount);

/// <summary>
/// VendorListItem + email/paymentTerms/notes/createdAt plus the embedded catalog sections and
/// their documents (E5-05, depends on E6).
/// </summary>
public sealed record VendorDetailDto(
    Guid Id,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Region,
    IReadOnlyList<CategoryRefDto> Categories,
    StatusRefDto Status,
    string? Moq,
    string? LeadTime,
    decimal? ReliabilityRating,
    int CatalogCount,
    string? Email,
    string? PaymentTerms,
    string? Notes,
    DateTime CreatedAt,
    IReadOnlyList<CatalogSectionDto> CatalogSections);

public sealed record VendorListResultDto(IReadOnlyList<VendorListItemDto> Items, int Page, int PageSize, int TotalCount);

public sealed record CreateVendorRequest(
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Region,
    Guid StatusId,
    IReadOnlyList<Guid>? CategoryIds,
    string? Moq,
    string? LeadTime,
    string? PaymentTerms,
    decimal? ReliabilityRating,
    string? Notes);

public sealed record UpdateVendorRequest(
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Region,
    Guid StatusId,
    IReadOnlyList<Guid>? CategoryIds,
    string? Moq,
    string? LeadTime,
    string? PaymentTerms,
    decimal? ReliabilityRating,
    string? Notes);

/// <summary>All four filters plus search/paging from the binding <c>GET /vendors</c> contract.</summary>
public sealed record VendorListQuery(
    string? Search,
    int Page,
    int PageSize,
    Guid? CategoryId,
    string? Region,
    Guid? StatusId);
