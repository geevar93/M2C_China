namespace SourcingOps.Application.Interfaces;

/// <summary>
/// Phase 2 seam (ACTION_PLAN E9-09, FR-WA-07): abstracts *how* a dispatch message reaches the
/// customer, independent of the dispatch log and the calling screens. The only Phase 1
/// implementation (<c>Infrastructure.Dispatching.WhatsAppDeepLinkSender</c>) returns a `wa.me`
/// click-to-chat deep link for a staff member to open manually — FSD A2 is explicit that no
/// WhatsApp Business API dependency exists in Phase 1. A future Business-API-backed
/// implementation can send the message directly from <see cref="Prepare"/> (or a new member
/// added to this interface) without <c>DispatchService</c>, the `dispatches` log table, or the
/// calling screens changing at all — the seam is deliberately this one interface, one
/// implementation; nothing else is introduced ahead of actually needing it.
/// </summary>
public interface IDispatchMessageSender
{
    /// <summary>
    /// Builds whatever the caller needs to complete the send. Phase 1: a `wa.me` deep-link URL
    /// the staff member opens to send <paramref name="message"/> to <paramref name="customerPhoneE164"/>
    /// themselves; the dispatch is recorded as sent only once the staff member confirms via
    /// <c>POST /api/v1/dispatch-log</c> (E9-02) — this call never talks to a live channel itself.
    /// </summary>
    /// <param name="customerPhoneE164">
    /// The customer's phone as stored (E4-04's normalised international format, e.g. "+919825041122").
    /// </param>
    /// <param name="message">The pre-filled message text, not yet URL-encoded.</param>
    DispatchSendPreparation Prepare(string customerPhoneE164, string message);
}

/// <summary>Result of <see cref="IDispatchMessageSender.Prepare"/>. Phase 1 only ever populates <see cref="DeepLinkUrl"/>.</summary>
public sealed record DispatchSendPreparation(string DeepLinkUrl);
