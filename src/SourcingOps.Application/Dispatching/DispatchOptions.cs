namespace SourcingOps.Application.Dispatching;

/// <summary>
/// Plain settings POCO — same "not <c>IOptions&lt;T&gt;</c>, keep Application framework-light"
/// pattern as <see cref="Crm.CustomerOptions"/> and <see cref="Catalog.CatalogUploadOptions"/>.
/// Backs FR-WA-05/FR-ADM-05 (ACTION_PLAN E9-06): a configurable message template with
/// customer-name and catalog-name placeholders, changeable without a deploy per FSD §3.3 —
/// though unlike the lookup-table master data elsewhere, a single free-text template string is
/// exactly the kind of setting the coordinator's other options POCOs already model as config,
/// not as a database row.
/// </summary>
public sealed class DispatchOptions
{
    /// <summary>
    /// Config key: <c>Dispatch:MessageTemplate</c>. <see cref="CustomerNamePlaceholder"/> and
    /// <see cref="CatalogNamePlaceholder"/> are substituted verbatim; any other text is passed
    /// through unchanged.
    /// </summary>
    public string MessageTemplate { get; set; } =
        "Hi {CustomerName}, sharing our catalog \"{CatalogName}\" with you. Please let us know if you have any questions!";

    public const string CustomerNamePlaceholder = "{CustomerName}";
    public const string CatalogNamePlaceholder = "{CatalogName}";

    /// <summary>E9-06: the rendered default — still fully editable by the caller before send (E9-04's dialog), never forced server-side.</summary>
    public string Render(string customerName, string catalogName) =>
        MessageTemplate.Replace(CustomerNamePlaceholder, customerName).Replace(CatalogNamePlaceholder, catalogName);
}
