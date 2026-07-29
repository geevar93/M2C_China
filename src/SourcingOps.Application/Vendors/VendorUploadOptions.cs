namespace SourcingOps.Application.Vendors;

/// <summary>
/// Plain settings POCO for vendor-document uploads (ACTION_PLAN E5-07) — same pattern as
/// <see cref="Catalog.CatalogUploadOptions"/>: a separate type from Infrastructure's
/// <c>FileStorageOptions</c> because Application must not depend on Infrastructure
/// (TECH_SPEC §4.1), both bound independently from the same "Storage" config section
/// (<see cref="DependencyInjection.AddApplication"/>).
/// </summary>
public sealed class VendorUploadOptions
{
    public long MaxUploadSizeBytes { get; set; } = 20 * 1024 * 1024; // 20 MB default, matches FileStorageOptions
}
