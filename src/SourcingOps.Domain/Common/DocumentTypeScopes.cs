namespace SourcingOps.Domain.Common;

/// <summary>
/// The fixed set of values <see cref="Entities.DocumentType.Scope"/> may hold (deviation
/// D-f). A structural discriminator naming a code path, not business-configurable master
/// data — see that property's doc comment for why this is deliberately not a further lookup
/// table despite the DoD's FK rule.
/// </summary>
public static class DocumentTypeScopes
{
    /// <summary>Vendor compliance documents (E5-07): business licence, quality certificate, test report.</summary>
    public const string Vendor = "Vendor";

    /// <summary>Shipment reference documents (E7-09): packing list, bill of lading, airway bill, invoice.</summary>
    public const string Shipment = "Shipment";

    public static readonly string[] All = [Vendor, Shipment];

    public static bool IsValid(string? scope) => scope is not null && All.Contains(scope);
}
