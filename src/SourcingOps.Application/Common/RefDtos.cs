namespace SourcingOps.Application.Common;

/// <summary>
/// Nested lookup shapes for the M4 (E5/E6) Vendor/Catalog binding contract — deliberately
/// different from the CRM track's convention (<c>CustomerListItemDto</c> etc. return bare
/// master-data IDs and let the frontend's MasterDataService resolve labels). The coordinator
/// fixed vendors/catalog-sections to embed the resolved lookup object instead, so these two
/// shapes are shared between <c>Vendors</c> and <c>Catalog</c> rather than duplicated:
/// <see cref="StatusRefDto"/> for every status/label lookup (matches <c>LookupItemDto</c>'s
/// Code/Label pair, just narrowed to what a list/detail row needs) and
/// <see cref="CategoryRefDto"/> for categories, which use <c>Name</c> instead of
/// <c>Code</c>/<c>Label</c> (TECH_SPEC §6) — preserved here deliberately, not "fixed".
/// </summary>
public sealed record StatusRefDto(Guid Id, string Code, string Label);

public sealed record CategoryRefDto(Guid Id, string Name);
