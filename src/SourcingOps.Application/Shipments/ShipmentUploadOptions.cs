namespace SourcingOps.Application.Shipments;

/// <summary>
/// Plain settings POCO for shipment reference-document uploads (ACTION_PLAN E7-09) — same
/// pattern as <see cref="Catalog.CatalogUploadOptions"/> and
/// <see cref="Vendors.VendorUploadOptions"/>: a separate type from Infrastructure's
/// <c>FileStorageOptions</c> because Application must not depend on Infrastructure
/// (TECH_SPEC §4.1), all bound independently from the same "Storage" config section
/// (<see cref="DependencyInjection.AddApplication"/>).
/// </summary>
public sealed class ShipmentUploadOptions
{
    public long MaxUploadSizeBytes { get; set; } = 20 * 1024 * 1024; // 20 MB default, matches FileStorageOptions
}
