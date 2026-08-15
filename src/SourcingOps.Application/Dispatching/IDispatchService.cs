namespace SourcingOps.Application.Dispatching;

/// <summary>Implements ACTION_PLAN E9-01, E9-02, E9-06, E9-07 behind <c>/api/v1/dispatch-log</c> and <c>/api/v1/catalog-documents/{id}/dispatches</c>.</summary>
public interface IDispatchService
{
    /// <summary>
    /// E9-01/E9-06/E9-10: mints a temporary public share link for the document, renders the
    /// configured template around it, and builds the `wa.me` deep link for this customer.
    /// Exactly one of <paramref name="catalogDocumentId"/>/<paramref name="invoiceId"/> must be
    /// supplied — both or neither throws <see cref="Common.AppValidationException"/>, mirroring
    /// <see cref="CreateAsync"/>. Null return means the customer or the document was not found;
    /// a target that exists but has no stored file (a Draft invoice) throws instead, since that
    /// is a fixable state rather than a missing record.
    /// </summary>
    Task<DispatchComposeDto?> ComposeAsync(
        Guid customerId, Guid? catalogDocumentId, Guid? invoiceId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// E9-02: records a dispatch. <paramref name="actorUserId"/> — sourced from the caller's
    /// token, never the request body — becomes <c>Dispatch.StaffUserId</c>. Throws
    /// <see cref="Common.AppValidationException"/> if the customer id or catalog document id is
    /// unknown, or the message is blank.
    /// </summary>
    Task<DispatchLogDto> CreateAsync(CreateDispatchLogRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>E9-07: newest-first dispatch history for a catalog document. Null return means the catalog document id was not found.</summary>
    Task<IReadOnlyList<DispatchHistoryEntryDto>?> GetDocumentHistoryAsync(Guid catalogDocumentId, CancellationToken ct = default);
}
