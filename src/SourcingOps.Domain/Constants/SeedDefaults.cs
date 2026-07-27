namespace SourcingOps.Domain.Constants;

/// <summary>
/// Default configurable-master-data rows seeded at first startup (TECH_SPEC §4.3, §6;
/// ACTION_PLAN E1-03/E3-05/E3-06/E3-07). These are seed defaults, not hard-coded
/// application logic — Super Admin can add/rename/retire rows afterwards via
/// MasterDataController (E3-03, later milestone). Every value here is written into a
/// lookup table row on first run and never referenced directly by feature code.
/// </summary>
public static class SeedDefaults
{
    public const string ServiceTypeCif = "CIF";
    public const string ServiceTypeFreightOnly = "FREIGHT_ONLY";

    // Codes are TECH_SPEC §6's; labels are the approved prototype's wording
    // (Source/Sourcing Ops Platform.dc.html's SVC map — CIF / "FREIGHT-ONLY"), per
    // coordinator direction so the two tracks agree on display text.
    public static readonly (string Code, string Label, int SortOrder)[] ServiceTypes =
    [
        (ServiceTypeCif, "CIF", 1),
        (ServiceTypeFreightOnly, "Freight-only", 2)
    ];

    /// <summary>
    /// The six categories, seeded verbatim (spelling included — British "Jewellery",
    /// not "Jewelry") from the approved prototype's hard-coded CATS list
    /// (Source/Sourcing Ops Platform.dc.html), per coordinator direction, since the
    /// frontend's dropdowns resolve against these exact strings. TECH_SPEC/FSD text
    /// does not spell out the six names, so there is no discrepancy to reconcile.
    /// </summary>
    public static readonly (string Name, int SortOrder)[] Categories =
    [
        ("Jewellery", 1),
        ("Furniture", 2),
        ("Stationery", 3),
        ("Handbags", 4),
        ("Electronics", 5),
        ("Tools", 6)
    ];

    /// <summary>Lead/customer pipeline stages (code, label, sortOrder).</summary>
    public static readonly (string Code, string Label, int SortOrder)[] LeadStatuses =
    [
        ("NEW", "New", 1),
        ("QUALIFIED", "Qualified", 2),
        ("ACTIVE", "Active", 3),
        ("WON", "Won", 4),
        ("LOST", "Lost", 5),
        ("DORMANT", "Dormant", 6)
    ];

    /// <summary>
    /// Shipment lifecycle stages (code, label, sortOrder). Code for the third stage is
    /// "IN TRANSIT" with a literal space — matches the prototype's StatusStyleService
    /// key verbatim (Source/Sourcing Ops Platform.dc.html's ST map), not an
    /// underscore/hyphen variant, per coordinator direction.
    /// </summary>
    public static readonly (string Code, string Label, int SortOrder)[] ShipmentStatuses =
    [
        ("PACKED", "Packed", 1),
        ("DISPATCHED", "Dispatched", 2),
        ("IN TRANSIT", "In Transit", 3),
        ("DELIVERED", "Delivered", 4)
    ];

    /// <summary>Invoice lifecycle (code, label, sortOrder).</summary>
    public static readonly (string Code, string Label, int SortOrder)[] InvoiceStatuses =
    [
        ("DRAFT", "Draft", 1),
        ("ISSUED", "Issued", 2),
        ("PAID", "Paid", 3),
        ("CANCELLED", "Cancelled", 4)
    ];

    /// <summary>
    /// Vendor lifecycle state (code, label, sortOrder). NOT itemized in TECH_SPEC §6 or
    /// ACTION_PLAN E3 as a seeded lookup — TECH_SPEC §6 lists vendors.status as a plain
    /// column. Added here as an explicit E1-02 deviation so vendor status follows the
    /// same "FK to a lookup table, never free text" rule applied to every other
    /// status/category/service-type column; see the assumptions section of the build
    /// report for the open item this raises. Code for the middle stage is "ON-HOLD"
    /// (hyphen) to match the prototype's StatusStyleService key verbatim, per
    /// coordinator direction.
    /// </summary>
    public static readonly (string Code, string Label, int SortOrder)[] VendorStatuses =
    [
        ("ACTIVE", "Active", 1),
        ("ON-HOLD", "On Hold", 2),
        ("INACTIVE", "Inactive", 3)
    ];
}
