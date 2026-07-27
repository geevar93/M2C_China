namespace SourcingOps.Application.MasterData;

/// <summary>
/// Identifies one of the six configurable master-data collections (TECH_SPEC §6; the
/// coordinator's binding <c>/api/v1/master-data/{collection}</c> contract). <c>Categories</c>
/// is the one collection whose entity (<see cref="SourcingOps.Domain.Entities.Category"/>)
/// does not implement <see cref="SourcingOps.Domain.Common.ILookupEntity"/> — it has
/// <c>Name</c> instead of <c>Code</c>/<c>Label</c> — so <see cref="MasterDataService"/>
/// special-cases it explicitly wherever this enum is switched over, rather than forcing it
/// into the shared generic path.
/// </summary>
public enum MasterDataCollectionKey
{
    Categories,
    ServiceTypes,
    LeadStatuses,
    ShipmentStatuses,
    InvoiceStatuses,
    VendorStatuses
}

/// <summary>Maps the contract's kebab-case URL segments (e.g. <c>service-types</c>) to <see cref="MasterDataCollectionKey"/>.</summary>
public static class MasterDataCollectionKeyExtensions
{
    private static readonly IReadOnlyDictionary<string, MasterDataCollectionKey> BySegment =
        new Dictionary<string, MasterDataCollectionKey>(StringComparer.OrdinalIgnoreCase)
        {
            ["categories"] = MasterDataCollectionKey.Categories,
            ["service-types"] = MasterDataCollectionKey.ServiceTypes,
            ["lead-statuses"] = MasterDataCollectionKey.LeadStatuses,
            ["shipment-statuses"] = MasterDataCollectionKey.ShipmentStatuses,
            ["invoice-statuses"] = MasterDataCollectionKey.InvoiceStatuses,
            ["vendor-statuses"] = MasterDataCollectionKey.VendorStatuses
        };

    public static bool TryParse(string? segment, out MasterDataCollectionKey key)
    {
        if (segment is not null && BySegment.TryGetValue(segment, out var found))
        {
            key = found;
            return true;
        }

        key = default;
        return false;
    }
}
