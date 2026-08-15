namespace SourcingOps.Domain.Constants;

/// <summary>
/// The document kinds a <see cref="Entities.DocumentShareLink"/> can point at (E9-10).
///
/// These are exactly the two things <see cref="Entities.Dispatch"/> itself can target, which is
/// the boundary that keeps this list honest: a share link exists to be pasted into a dispatch
/// message, so a type that cannot be dispatched has nothing to mint a link from. Vendor
/// compliance documents (E5-07) and shipment reference documents (E7-09) are therefore
/// deliberately absent — adding either later is a resolver case in
/// <c>DocumentShareLinkService</c> plus a dispatch target, **not** a migration, because
/// <see cref="Entities.DocumentShareLink.TargetType"/> is a discriminator rather than an FK.
/// </summary>
public static class DocumentShareTargetTypes
{
    public const string CatalogDocument = "CatalogDocument";
    public const string Invoice = "Invoice";

    public static readonly string[] All = [CatalogDocument, Invoice];
}
