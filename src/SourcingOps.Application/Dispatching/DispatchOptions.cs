namespace SourcingOps.Application.Dispatching;

/// <summary>
/// Plain settings POCO — same "not <c>IOptions&lt;T&gt;</c>, keep Application framework-light"
/// pattern as <see cref="Crm.CustomerOptions"/> and <see cref="Catalog.CatalogUploadOptions"/>.
/// Backs FR-WA-05/FR-ADM-05 (ACTION_PLAN E9-06): a configurable message template with
/// customer-name and catalog-name placeholders, changeable without a deploy per FSD §3.3 —
/// though unlike the lookup-table master data elsewhere, a single free-text template string is
/// exactly the kind of setting the coordinator's other options POCOs already model as config,
/// not as a database row.
///
/// E9-10 added the share-link half: <see cref="ShareLinkLifetimeHours"/>,
/// <see cref="PublicBaseUrl"/>, and the <see cref="DocumentLinkPlaceholder"/> /
/// <see cref="LinkExpiryHoursPlaceholder"/> substitutions.
/// </summary>
public sealed class DispatchOptions
{
    /// <summary>
    /// Config key: <c>Dispatch:MessageTemplate</c>. The four placeholders below are substituted
    /// verbatim; any other text is passed through unchanged.
    /// </summary>
    /// <remarks>
    /// H-19: the owner asked that the copy say plainly that the link is temporary and that the
    /// recipient should download the file promptly, rather than only stating an expiry the reader
    /// may skim past. "Temporary link" leads, the hours follow as the specific, and the action
    /// asked for is <em>download</em>, not merely <em>open</em> — a viewed PDF is gone when the
    /// link expires; a downloaded one is not.
    /// </remarks>
    public string MessageTemplate { get; set; } =
        "Hi {CustomerName}, sharing our catalog \"{CatalogName}\" with you: {DocumentLink}\n\n" +
        "This is a temporary link — it opens the PDF directly on your phone and stops working after " +
        "{LinkExpiryHours} hours, so please download and save the file soon. " +
        "Let us know if you have any questions!";

    public const string CustomerNamePlaceholder = "{CustomerName}";
    public const string CatalogNamePlaceholder = "{CatalogName}";

    /// <summary>E9-10: the public, expiring URL to the document itself.</summary>
    public const string DocumentLinkPlaceholder = "{DocumentLink}";

    /// <summary>E9-10: <see cref="ShareLinkLifetimeHours"/>, so the note in the message stays true when the setting changes.</summary>
    public const string LinkExpiryHoursPlaceholder = "{LinkExpiryHours}";

    /// <summary>
    /// Config key: <c>Dispatch:ShareLinkLifetimeHours</c>. Default 48.
    ///
    /// **Why 48 and not 24 or a week.** The recipient is a wholesale buyer reading WhatsApp on a
    /// phone, often in a different timezone from the sender (FSD's China-to-India flow), and
    /// staff commonly send at the end of their working day. A 24-hour window can lapse before a
    /// message sent Friday evening is opened Monday morning; a week-long window leaves an
    /// unauthenticated URL live in a chat log long after the conversation moved on. 48 hours
    /// covers the realistic "read it tomorrow, or the day after" case while keeping the exposure
    /// window short, and it is the number the default template quotes to the recipient.
    /// </summary>
    public int ShareLinkLifetimeHours { get; set; } = 48;

    /// <summary>
    /// Config key: <c>Dispatch:PublicBaseUrl</c>. The origin the share URL is built against —
    /// the address the recipient's phone can reach, which is the deployed **frontend** origin
    /// (Caddy serves the SPA there and reverse-proxies <c>/api/*</c> to the API container,
    /// TECH_SPEC §7.2), not the API container's internal address. In local dev the same holds
    /// via the <c>ng serve</c> proxy. Left empty it falls back to <c>Cors:FrontendOrigin</c> at
    /// registration time, so a correctly-configured deployment needs no extra setting.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>The route the anonymous share endpoint is served on. Kept here so the URL is built in one place.</summary>
    public const string ShareLinkPathPrefix = "/api/v1/shared-documents/";

    public string BuildShareUrl(string token) => $"{PublicBaseUrl.TrimEnd('/')}{ShareLinkPathPrefix}{token}";

    /// <summary>
    /// E9-06: the rendered default — still fully editable by the caller before send (E9-04's
    /// dialog), never forced server-side.
    ///
    /// E9-10 behaviour worth knowing about: if an operator edits
    /// <see cref="MessageTemplate"/> in config and omits <see cref="DocumentLinkPlaceholder"/>,
    /// the link is **appended** rather than dropped. That is a deliberate exception to "pass
    /// other text through unchanged" — since E9-10 the link is the delivery mechanism, not
    /// decoration, so a template typo silently shipping link-less dispatches would break the
    /// feature quietly and look like a WhatsApp problem. Removing the placeholder is far more
    /// likely to be an accident than an instruction to send nothing.
    /// </summary>
    public string Render(string customerName, string catalogName, string documentLink)
    {
        var body = MessageTemplate
            .Replace(CustomerNamePlaceholder, customerName)
            .Replace(CatalogNamePlaceholder, catalogName)
            .Replace(LinkExpiryHoursPlaceholder, ShareLinkLifetimeHours.ToString())
            .Replace(DocumentLinkPlaceholder, documentLink);

        if (!MessageTemplate.Contains(DocumentLinkPlaceholder, StringComparison.Ordinal) && !string.IsNullOrEmpty(documentLink))
        {
            // Carries the same temporary-link warning as the default template (H-19) — an
            // operator who edited the placeholder out should not also lose the warning.
            body += $"\n\n{documentLink}\n(Temporary link — it stops working after {ShareLinkLifetimeHours} hours, so please download the file soon.)";
        }

        return body;
    }
}
