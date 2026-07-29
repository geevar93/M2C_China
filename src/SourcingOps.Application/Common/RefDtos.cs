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

/// <summary>
/// Resolved vendor reference for the M5 (E7) inventory track, which embeds its source vendor
/// the same way vendors/catalog-sections embed their status and category. A vendor has a
/// <c>Name</c> and no Code/Label pair, so it needs its own shape rather than reusing
/// <see cref="StatusRefDto"/>; deliberately distinct from <see cref="CategoryRefDto"/> despite
/// the identical field list, because collapsing them would make the two interchangeable at a
/// call site where they are not.
/// </summary>
public sealed record VendorRefDto(Guid Id, string Name);

/// <summary>
/// Resolved customer reference for the M5 shipment track — same reasoning as
/// <see cref="VendorRefDto"/>. <c>Name</c> carries the customer's business name when set,
/// falling back to the contact name, so a list row never renders blank.
/// </summary>
public sealed record CustomerRefDto(Guid Id, string Name);
