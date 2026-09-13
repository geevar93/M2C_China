namespace SourcingOps.Domain.Constants;

/// <summary>
/// GST state codes (the first two digits of a GSTIN) for every Indian state and union
/// territory. Place of supply is decided by comparing the seller's code with the buyer's:
/// same code is an intra-state supply (CGST + SGST), different codes is inter-state (IGST).
///
/// Held as a constant rather than a configurable lookup table — unlike the FSD §3.3
/// collections, this list is set by statute, not by the business, and a Super Admin editing
/// it could only ever make the tax split wrong.
///
/// DOMESTIC SUPPLIES ONLY. Export/overseas customers are deliberately out of scope for this
/// pass (they are zero-rated under LUT or carry IGST with a refund claim, and neither is
/// modelled). <c>InvoiceService</c> refuses to issue rather than guessing when a customer has
/// no state code.
/// </summary>
public static class IndianStateCodes
{
    /// <summary>State code -&gt; name, keyed by the two-digit GSTIN prefix.</summary>
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["01"] = "Jammu and Kashmir",
        ["02"] = "Himachal Pradesh",
        ["03"] = "Punjab",
        ["04"] = "Chandigarh",
        ["05"] = "Uttarakhand",
        ["06"] = "Haryana",
        ["07"] = "Delhi",
        ["08"] = "Rajasthan",
        ["09"] = "Uttar Pradesh",
        ["10"] = "Bihar",
        ["11"] = "Sikkim",
        ["12"] = "Arunachal Pradesh",
        ["13"] = "Nagaland",
        ["14"] = "Manipur",
        ["15"] = "Mizoram",
        ["16"] = "Tripura",
        ["17"] = "Meghalaya",
        ["18"] = "Assam",
        ["19"] = "West Bengal",
        ["20"] = "Jharkhand",
        ["21"] = "Odisha",
        ["22"] = "Chhattisgarh",
        ["23"] = "Madhya Pradesh",
        ["24"] = "Gujarat",
        ["26"] = "Dadra and Nagar Haveli and Daman and Diu",
        ["27"] = "Maharashtra",
        ["29"] = "Karnataka",
        ["30"] = "Goa",
        ["31"] = "Lakshadweep",
        ["32"] = "Kerala",
        ["33"] = "Tamil Nadu",
        ["34"] = "Puducherry",
        ["35"] = "Andaman and Nicobar Islands",
        ["36"] = "Telangana",
        ["37"] = "Andhra Pradesh",
        ["38"] = "Ladakh",
        ["97"] = "Other Territory"
    };

    public static bool IsValid(string? code) => code is not null && All.ContainsKey(code);

    public static string? NameFor(string? code) => code is not null && All.TryGetValue(code, out var name) ? name : null;

    /// <summary>
    /// The state code embedded in a GSTIN's first two digits, or null when the GSTIN is absent,
    /// malformed, or carries a prefix that is not a real state code. Used to default a state
    /// code that nobody has set by hand — never to override one that has been set.
    /// </summary>
    public static string? FromGstin(string? gstin)
    {
        if (string.IsNullOrWhiteSpace(gstin))
        {
            return null;
        }

        var trimmed = gstin.Trim();
        if (trimmed.Length < 2)
        {
            return null;
        }

        var prefix = trimmed[..2];
        return All.ContainsKey(prefix) ? prefix : null;
    }
}
