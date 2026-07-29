namespace SourcingOps.Application.Catalog;

/// <summary>
/// Plain settings POCO — same "not <c>IOptions&lt;T&gt;</c>, keep Application framework-light"
/// pattern as <see cref="Crm.CustomerOptions"/>. Deliberately a separate type from
/// Infrastructure's <c>FileStorageOptions</c> rather than Application referencing it directly:
/// Application must not depend on Infrastructure (TECH_SPEC §4.1's layering), so both POCOs
/// are bound from the same "Storage" config section independently and must be kept in sync by
/// convention (<see cref="DependencyInjection.AddApplication"/> and Infrastructure's
/// <c>AddInfrastructure</c> both read <c>Storage:MaxUploadSizeBytes</c>).
/// </summary>
public sealed class CatalogUploadOptions
{
    public long MaxUploadSizeBytes { get; set; } = 20 * 1024 * 1024; // 20 MB default, matches FileStorageOptions
}
