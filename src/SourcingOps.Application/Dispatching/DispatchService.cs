using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Constants;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Dispatching;

/// <summary>
/// Implements ACTION_PLAN E9-01, E9-02, E9-06, E9-07. The actual "send" is one click by the
/// staff member in their own WhatsApp client (E9-01/FSD A2 — no Business API dependency); this
/// service's job is to (a) prepare that click via <see cref="ComposeAsync"/> and (b) record that
/// it happened via <see cref="CreateAsync"/>. Delivery mechanics live entirely behind
/// <see cref="IDispatchMessageSender"/> (E9-09) — this class never builds a `wa.me` URL itself.
/// </summary>
public sealed class DispatchService : IDispatchService
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDispatchMessageSender _sender;
    private readonly IDocumentShareLinkService _shareLinks;
    private readonly DispatchOptions _options;

    public DispatchService(
        IAppDbContext db,
        IAuditLogger audit,
        IDispatchMessageSender sender,
        IDocumentShareLinkService shareLinks,
        DispatchOptions options)
    {
        _db = db;
        _audit = audit;
        _sender = sender;
        _shareLinks = shareLinks;
        _options = options;
    }

    // ---- Compose (E9-01, E9-06, E9-10) -------------------------------------------------

    /// <summary>
    /// E9-10 changed this method's job. It used to render text and build a `wa.me` URL; it now
    /// also mints the temporary public link to the document itself and substitutes it into the
    /// message, so the recipient can open the PDF from the chat instead of the staff member
    /// downloading and attaching it by hand. The link is minted here, at dialog-open time, and
    /// never eagerly for documents nobody is sharing.
    ///
    /// E9-10 also widened the target from catalog-document-only to the same
    /// exactly-one-of-two shape <see cref="CreateAsync"/> has had since M6/E8-06 — the dispatch
    /// log could already record an invoice send while compose could not prepare one, which is
    /// why the invoice screen's WhatsApp button is still inert.
    /// </summary>
    public async Task<DispatchComposeDto?> ComposeAsync(
        Guid customerId, Guid? catalogDocumentId, Guid? invoiceId, Guid actorUserId, CancellationToken ct = default)
    {
        var hasCatalog = catalogDocumentId.HasValue;
        var hasInvoice = invoiceId.HasValue;
        if (hasCatalog == hasInvoice) // both set, or neither
        {
            throw new AppValidationException("catalogDocumentId", "Exactly one of catalogDocumentId or invoiceId must be supplied.");
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
        {
            return null;
        }

        string documentName;
        string targetType;
        Guid targetId;

        if (hasCatalog)
        {
            var document = await _db.CatalogDocuments.Include(d => d.CatalogSection)
                .FirstOrDefaultAsync(d => d.Id == catalogDocumentId!.Value, ct);
            if (document is null)
            {
                return null;
            }

            documentName = document.CatalogSection.Title;
            targetType = DocumentShareTargetTypes.CatalogDocument;
            targetId = document.Id;
        }
        else
        {
            var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId!.Value, ct);
            if (invoice is null)
            {
                return null;
            }

            // The template's {CatalogName} placeholder is really "the document being sent" —
            // named for the only case that existed when E9-06 defined it. Rather than add a
            // second placeholder the operator would have to remember to use, an invoice fills
            // the same slot with a human-readable label.
            documentName = $"Invoice {invoice.InvoiceNumber}";
            targetType = DocumentShareTargetTypes.Invoice;
            targetId = invoice.Id;
        }

        // Throws AppValidationException (400) for a Draft invoice with no rendered PDF — a
        // failure the staff member can act on, unlike a link that 404s in the customer's chat.
        var shareLink = await _shareLinks.MintAsync(targetType, targetId, actorUserId, ct);

        var message = _options.Render(customer.Name, documentName, shareLink.Url);
        var prepared = _sender.Prepare(customer.Phone, message);

        return new DispatchComposeDto(message, prepared.DeepLinkUrl, shareLink);
    }

    // ---- Create (E9-02, M6/E8-06) -------------------------------------------------------

    /// <summary>
    /// M6/E8-06: <paramref name="request"/> must carry EXACTLY ONE of
    /// <see cref="CreateDispatchLogRequest.CatalogDocumentId"/> /
    /// <see cref="CreateDispatchLogRequest.InvoiceId"/> — mirroring the DB CHECK constraint on
    /// <c>Dispatch</c> itself (both/neither is a 400, not a constraint violation the caller
    /// only discovers at SaveChanges).
    /// </summary>
    public async Task<DispatchLogDto> CreateAsync(CreateDispatchLogRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var message = (request.Message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new AppValidationException("message", "Message is required.");
        }

        var hasCatalog = request.CatalogDocumentId.HasValue;
        var hasInvoice = request.InvoiceId.HasValue;
        if (hasCatalog == hasInvoice) // both set, or neither
        {
            throw new AppValidationException("catalogDocumentId", "Exactly one of catalogDocumentId or invoiceId must be supplied.");
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new AppValidationException("customerId", "Unknown customer.");

        CatalogDocument? document = null;
        Invoice? invoice = null;

        if (hasCatalog)
        {
            document = await _db.CatalogDocuments.Include(d => d.CatalogSection)
                .FirstOrDefaultAsync(d => d.Id == request.CatalogDocumentId!.Value, ct)
                ?? throw new AppValidationException("catalogDocumentId", "Unknown catalog document.");
        }
        else
        {
            invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == request.InvoiceId!.Value, ct)
                ?? throw new AppValidationException("invoiceId", "Unknown invoice.");
        }

        var dispatch = new Dispatch
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            CatalogDocumentId = document?.Id,
            InvoiceId = invoice?.Id,
            StaffUserId = actorUserId,
            Message = message,
            SentAt = DateTime.UtcNow
        };
        _db.Dispatches.Add(dispatch);
        await _db.SaveChangesAsync(ct);

        var staffUser = await _db.Users.FindAsync([actorUserId], ct);

        await _audit.LogAsync(actorUserId, "DispatchLogged", "Dispatch", dispatch.Id.ToString(),
            new { customer.Id, CustomerName = customer.Name, CatalogDocumentId = document?.Id, InvoiceId = invoice?.Id }, ct);

        return new DispatchLogDto(
            dispatch.Id, customer.Id, customer.Name,
            document?.Id, document?.CatalogSection.Title, document?.OriginalFilename,
            invoice?.Id, invoice?.InvoiceNumber,
            actorUserId, staffUser?.Name ?? "(unknown)", dispatch.Message,
            AsUtc(dispatch.SentAt));
    }

    // ---- Sent-to history (E9-07) -------------------------------------------------------

    public async Task<IReadOnlyList<DispatchHistoryEntryDto>?> GetDocumentHistoryAsync(Guid catalogDocumentId, CancellationToken ct = default)
    {
        var documentExists = await _db.CatalogDocuments.AnyAsync(d => d.Id == catalogDocumentId, ct);
        if (!documentExists)
        {
            return null;
        }

        var dispatches = await _db.Dispatches
            .Include(d => d.Customer)
            .Include(d => d.StaffUser)
            .Where(d => d.CatalogDocumentId == catalogDocumentId)
            .OrderByDescending(d => d.SentAt)
            .ToListAsync(ct);

        return dispatches
            .Select(d => new DispatchHistoryEntryDto(
                d.Id, d.CustomerId, d.Customer?.Name ?? "(unknown)", d.StaffUserId, d.StaffUser?.Name ?? "(unknown)", AsUtc(d.SentAt)))
            .ToList();
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
