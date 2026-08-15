namespace SourcingOps.Application.Dispatching;

/// <summary>
/// E9-01/E9-06: what a caller needs to open the dispatch dialog — the rendered template default
/// (still editable, per E9-06's explicit "text remains editable before sending") and the `wa.me`
/// deep link built from the customer's stored phone (E9-01). Returned by
/// <c>GET /api/v1/dispatch-log/compose</c>.
///
/// E9-10 added <see cref="ShareLink"/>. Note that <see cref="Message"/> already contains the
/// share URL — the separate field exists so the dialog can show the expiry and offer a revoke
/// handle without parsing the message text, not because the caller needs to splice it in.
/// </summary>
public sealed record DispatchComposeDto(string Message, string DeepLinkUrl, DocumentShareLinkDto ShareLink);

/// <summary>
/// E9-02: <c>POST /api/v1/dispatch-log</c> body. Deliberately has no staff/actor field — the
/// staff user always comes from the caller's token (<c>User.GetRequiredUserId()</c>), never from
/// the request body, so a caller cannot attribute a dispatch to someone else.
///
/// M6/E8-06 added <see cref="InvoiceId"/> alongside the original <see cref="CatalogDocumentId"/>
/// — exactly one of the two must be supplied (<c>DispatchService.CreateAsync</c> rejects both
/// or neither with 400), mirroring the DB-enforced invariant on <c>Dispatch</c> itself.
/// </summary>
public sealed record CreateDispatchLogRequest(Guid CustomerId, Guid? CatalogDocumentId, Guid? InvoiceId, string Message);

/// <summary>
/// The recorded dispatch log entry, with display names resolved so the caller does not need a
/// second round trip. Exactly one of the two target pairs
/// (<see cref="CatalogDocumentId"/>/<see cref="CatalogName"/>/<see cref="CatalogDocumentFilename"/>
/// vs <see cref="InvoiceId"/>/<see cref="InvoiceNumber"/>) is non-null on any given row —
/// M6/E8-06 addition, mirroring <c>Dispatch</c>'s own CHECK constraint.
/// </summary>
public sealed record DispatchLogDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    Guid? CatalogDocumentId,
    string? CatalogName,
    string? CatalogDocumentFilename,
    Guid? InvoiceId,
    string? InvoiceNumber,
    Guid StaffUserId,
    string StaffUserName,
    string Message,
    DateTime SentAtUtc);

/// <summary>
/// E9-07: one row of a catalog document's "sent to" history — which customer, which staff
/// member, and when. Newest first.
/// </summary>
public sealed record DispatchHistoryEntryDto(
    Guid DispatchId,
    Guid CustomerId,
    string CustomerName,
    Guid StaffUserId,
    string StaffUserName,
    DateTime SentAtUtc);
