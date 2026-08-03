using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Constants;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Invoicing;

/// <summary>
/// Implements ACTION_PLAN E8-01…E8-08 per the M6 contract. Structurally mirrors
/// <c>ShipmentService</c> — list queries materialize the current page and map client-side,
/// every mutation commits with one <c>SaveChangesAsync</c>, number generation reuses
/// <c>SaveWithGeneratedReferenceAsync</c>'s retry-on-23505 shape verbatim (contract §1: "do not
/// invent a second approach").
/// </summary>
public sealed class InvoiceService : IInvoiceService
{
    /// <summary>Same rationale as <c>ShipmentService.MaxReferenceAttempts</c> — see its doc comment.</summary>
    private const int MaxReferenceAttempts = 5;
    private const string DefaultPrefix = "INV";

    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IFileStorage _fileStorage;
    private readonly IInvoicePdfRenderer _pdfRenderer;

    public InvoiceService(IAppDbContext db, IAuditLogger audit, IFileStorage fileStorage, IInvoicePdfRenderer pdfRenderer)
    {
        _db = db;
        _audit = audit;
        _fileStorage = fileStorage;
        _pdfRenderer = pdfRenderer;
    }

    // ---- List / filter (E8-05) --------------------------------------------------------

    public async Task<InvoiceListResultDto> ListAsync(InvoiceListQuery query, CancellationToken ct = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 200 ? 25 : query.PageSize; // M6 contract §3: default 25

        if (query.FromDate.HasValue && query.ToDate.HasValue && query.FromDate.Value > query.ToDate.Value)
        {
            throw new AppValidationException("fromDate", "'fromDate' must not be later than 'toDate'.");
        }

        // Everything EXCEPT the status filter — E7-08's semantics, reused verbatim (M6 contract §3).
        var withoutStatus = _db.Invoices.AsQueryable();

        if (query.CustomerId.HasValue)
        {
            withoutStatus = withoutStatus.Where(i => i.CustomerId == query.CustomerId.Value);
        }
        if (query.ServiceTypeId.HasValue)
        {
            withoutStatus = withoutStatus.Where(i => i.Customer.ServiceTypeId == query.ServiceTypeId.Value);
        }
        if (query.FromDate.HasValue)
        {
            var from = query.FromDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            withoutStatus = withoutStatus.Where(i => i.InvoiceDate >= from);
        }
        if (query.ToDate.HasValue)
        {
            var to = query.ToDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            withoutStatus = withoutStatus.Where(i => i.InvoiceDate <= to);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            withoutStatus = withoutStatus.Where(i =>
                i.InvoiceNumber.ToLower().Contains(term) ||
                i.Customer.Name.ToLower().Contains(term) ||
                (i.Customer.BusinessName != null && i.Customer.BusinessName.ToLower().Contains(term)));
        }

        var statusCounts = await BuildStatusCountsAsync(withoutStatus, ct);

        var filtered = query.StatusId.HasValue
            ? withoutStatus.Where(i => i.StatusId == query.StatusId.Value)
            : withoutStatus;

        var totalCount = await filtered.CountAsync(ct);

        var pageEntities = await filtered
            .Include(i => i.Customer).ThenInclude(c => c.ServiceType)
            .Include(i => i.Status)
            .Include(i => i.Shipment)
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = pageEntities.Select(MapListItem).ToList();
        return new InvoiceListResultDto(items, page, pageSize, totalCount, statusCounts);
    }

    private async Task<List<InvoiceStatusCountDto>> BuildStatusCountsAsync(IQueryable<Invoice> source, CancellationToken ct)
    {
        var counts = await source
            .GroupBy(i => i.StatusId)
            .Select(g => new { StatusId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countsById = counts.ToDictionary(c => c.StatusId, c => c.Count);

        var statuses = await _db.InvoiceStatuses.ToListAsync(ct);

        return statuses
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Code, StringComparer.Ordinal)
            .Select(s => new InvoiceStatusCountDto(s.Id, s.Code, s.Label, s.SortOrder, countsById.GetValueOrDefault(s.Id, 0)))
            .ToList();
    }

    // ---- Get one -------------------------------------------------------------------

    public async Task<InvoiceDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var invoice = await LoadWithNavigationsAsync(id, ct);
        return invoice is null ? null : MapDetail(invoice);
    }

    // ---- Create (E8-01) --------------------------------------------------------------

    public async Task<InvoiceDetailDto> CreateAsync(CreateInvoiceRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var customer = await _db.Customers.Include(c => c.ServiceType).FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new AppValidationException("customerId", "Unknown customer.");

        var shipment = await ResolveShipmentAsync(request.ShipmentId, customer.Id, ct);

        ValidateAmounts(request.Amount, request.TaxAmount);
        var currency = NormalizeCurrency(request.Currency);

        var draftStatus = await _db.InvoiceStatuses.FirstOrDefaultAsync(s => s.Code == InvoiceStatusCodes.Draft, ct)
            ?? throw new AppValidationException("statusId", $"No '{InvoiceStatusCodes.Draft}' invoice status is configured.");

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Customer = customer,
            ShipmentId = shipment?.Id,
            Shipment = shipment,
            InvoiceDate = request.InvoiceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            LineDescription = Trim(request.LineDescription),
            Amount = request.Amount,
            TaxAmount = request.TaxAmount,
            Currency = currency,
            StatusId = draftStatus.Id,
            Status = draftStatus,
            CreatedByUserId = actorUserId,
            CreatedAt = DateTime.UtcNow
        };

        // D-e precedent: the opening status gets a history row at creation, so the detail
        // screen's history never starts empty.
        invoice.StatusHistory.Add(new InvoiceStatusHistory
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            StatusId = draftStatus.Id,
            Status = draftStatus,
            ChangedByUserId = actorUserId,
            ChangedAt = DateTime.UtcNow,
            Note = "Invoice created."
        });

        _db.Invoices.Add(invoice);
        await SaveWithGeneratedNumberAsync(invoice, ct);

        await _audit.LogAsync(actorUserId, "InvoiceCreated", "Invoice", invoice.Id.ToString(), new
        {
            invoice.InvoiceNumber,
            CustomerName = ResolveCustomerName(customer),
            ShipmentReference = shipment?.Reference,
            invoice.Amount,
            invoice.TaxAmount,
            invoice.Currency
        }, ct);

        var reloaded = await LoadWithNavigationsAsync(invoice.Id, ct);
        return MapDetail(reloaded ?? invoice);
    }

    // ---- Update (E8-01, editable only in Draft) ---------------------------------------

    public async Task<InvoiceDetailDto?> UpdateAsync(Guid id, UpdateInvoiceRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var invoice = await LoadWithNavigationsAsync(id, ct);
        if (invoice is null)
        {
            return null;
        }

        EnsureDraft(invoice);

        var customer = await _db.Customers.Include(c => c.ServiceType).FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new AppValidationException("customerId", "Unknown customer.");
        var shipment = await ResolveShipmentAsync(request.ShipmentId, customer.Id, ct);

        ValidateAmounts(request.Amount, request.TaxAmount);
        var currency = NormalizeCurrency(request.Currency);

        invoice.CustomerId = customer.Id;
        invoice.Customer = customer;
        invoice.ShipmentId = shipment?.Id;
        invoice.Shipment = shipment;
        invoice.InvoiceDate = request.InvoiceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        invoice.LineDescription = Trim(request.LineDescription);
        invoice.Amount = request.Amount;
        invoice.TaxAmount = request.TaxAmount;
        invoice.Currency = currency;

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "InvoiceUpdated", "Invoice", invoice.Id.ToString(), new
        {
            invoice.InvoiceNumber,
            CustomerName = ResolveCustomerName(customer),
            ShipmentReference = shipment?.Reference,
            invoice.Amount,
            invoice.TaxAmount,
            invoice.Currency
        }, ct);

        return MapDetail(invoice);
    }

    // ---- Status transition (E8-02, E8-03) ----------------------------------------------

    public async Task<InvoiceDetailDto?> ChangeStatusAsync(Guid id, ChangeInvoiceStatusRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var invoice = await LoadWithNavigationsAsync(id, ct);
        if (invoice is null)
        {
            return null;
        }

        var targetStatus = await _db.InvoiceStatuses.FindAsync([request.StatusId], ct)
            ?? throw new AppValidationException("statusId", "Unknown invoice status.");

        var fromCode = invoice.Status.Code;
        var toCode = targetStatus.Code;

        EnsureLegalTransition(fromCode, toCode);

        // DRAFT -> ISSUED renders and stores the PDF (M6 contract §5). Done BEFORE the entity
        // is mutated so a rendering failure (missing CompanySettings) leaves the invoice
        // untouched rather than half-transitioned.
        if (string.Equals(fromCode, InvoiceStatusCodes.Draft, StringComparison.Ordinal) &&
            string.Equals(toCode, InvoiceStatusCodes.Issued, StringComparison.Ordinal))
        {
            await RenderAndStorePdfAsync(invoice, ct);
        }

        invoice.StatusId = targetStatus.Id;
        invoice.Status = targetStatus;

        var history = new InvoiceStatusHistory
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            StatusId = targetStatus.Id,
            Status = targetStatus,
            ChangedByUserId = actorUserId,
            ChangedAt = DateTime.UtcNow,
            Note = Trim(request.Note)
        };
        _db.InvoiceStatusHistory.Add(history);

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "InvoiceStatusChanged", "Invoice", invoice.Id.ToString(),
            new { invoice.InvoiceNumber, FromStatusCode = fromCode, ToStatusCode = toCode, history.Note }, ct);

        var reloaded = await LoadWithNavigationsAsync(invoice.Id, ct);
        return MapDetail(reloaded ?? invoice);
    }

    // ---- Mark paid (E8-07) --------------------------------------------------------------

    public async Task<InvoiceDetailDto?> MarkPaidAsync(Guid id, MarkInvoicePaidRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var invoice = await LoadWithNavigationsAsync(id, ct);
        if (invoice is null)
        {
            return null;
        }

        var fromCode = invoice.Status.Code;
        if (!string.Equals(fromCode, InvoiceStatusCodes.Issued, StringComparison.Ordinal))
        {
            throw new InvoiceConflictException(
                "Invalid invoice status transition.",
                $"An invoice can only be marked paid from '{InvoiceStatusCodes.Issued}'; this invoice is currently '{fromCode}'.",
                new Dictionary<string, object?> { ["fromStatus"] = fromCode, ["toStatus"] = InvoiceStatusCodes.Paid });
        }

        var paidStatus = await _db.InvoiceStatuses.FirstOrDefaultAsync(s => s.Code == InvoiceStatusCodes.Paid, ct)
            ?? throw new AppValidationException("statusId", $"No '{InvoiceStatusCodes.Paid}' invoice status is configured.");

        invoice.StatusId = paidStatus.Id;
        invoice.Status = paidStatus;
        invoice.PaidAt = request.PaidAt.HasValue ? AsUtc(request.PaidAt.Value) : DateTime.UtcNow;
        invoice.PaidReference = Trim(request.PaidReference);

        var history = new InvoiceStatusHistory
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            StatusId = paidStatus.Id,
            Status = paidStatus,
            ChangedByUserId = actorUserId,
            ChangedAt = DateTime.UtcNow,
            Note = invoice.PaidReference is null ? "Marked paid." : $"Marked paid. Reference: {invoice.PaidReference}"
        };
        _db.InvoiceStatusHistory.Add(history);

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "InvoiceMarkedPaid", "Invoice", invoice.Id.ToString(),
            new { invoice.InvoiceNumber, invoice.PaidAt, invoice.PaidReference }, ct);

        var reloaded = await LoadWithNavigationsAsync(invoice.Id, ct);
        return MapDetail(reloaded ?? invoice);
    }

    // ---- PDF download (E8-03) ------------------------------------------------------------

    public async Task<InvoicePdfDownload?> GetPdfAsync(Guid id, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null)
        {
            return null;
        }

        if (invoice.PdfFilePath is null)
        {
            throw new InvoiceConflictException(
                "PDF not available.",
                "This invoice has not been issued yet, so no PDF has been generated.",
                new Dictionary<string, object?> { ["currentStatus"] = (await _db.InvoiceStatuses.FindAsync([invoice.StatusId], ct))?.Code });
        }

        var stream = await _fileStorage.OpenReadAsync(invoice.PdfFilePath, ct);
        return new InvoicePdfDownload(stream, $"{invoice.InvoiceNumber}.pdf");
    }

    // ---- PDF rendering (E8-03, M6 contract §0) -------------------------------------------

    /// <summary>
    /// Fails LOUDLY (AppValidationException -> 400) when <c>CompanySettings</c> is absent or a
    /// required field is still null — CompanySettings' own doc comment mandates this literally,
    /// and the M6 contract restates it as a gating rule. Never renders a blank/placeholder
    /// value onto what is meant to be a real financial document.
    /// </summary>
    private async Task RenderAndStorePdfAsync(Invoice invoice, CancellationToken ct)
    {
        var settings = await _db.CompanySettings.FindAsync([CompanySettings.SingletonId], ct);
        if (settings is null || string.IsNullOrWhiteSpace(settings.LegalEntityName) || string.IsNullOrWhiteSpace(settings.RegisteredAddress))
        {
            throw new AppValidationException("companySettings",
                "Company billing details are not configured (legal entity name and registered address are required). " +
                "A Super Admin must set these under Admin > Company Settings before an invoice can be issued.");
        }

        InvoicePdfBankDetails? bankDetails = null;
        if (!string.IsNullOrWhiteSpace(settings.BankAccountName) &&
            !string.IsNullOrWhiteSpace(settings.BankAccountNumber) &&
            !string.IsNullOrWhiteSpace(settings.BankIfsc))
        {
            // A partial bank block is worse than none (M6 contract §0) — only rendered when
            // all three of name/number/IFSC are present.
            bankDetails = new InvoicePdfBankDetails(settings.BankAccountName, settings.BankAccountNumber, settings.BankIfsc, settings.BankBranch);
        }

        var model = new InvoicePdfModel(
            invoice.InvoiceNumber,
            DateOnly.FromDateTime(invoice.InvoiceDate),
            ResolveCustomerName(invoice.Customer),
            invoice.Customer.City,
            invoice.Customer.Region,
            invoice.LineDescription,
            invoice.Amount,
            invoice.TaxAmount,
            invoice.Amount + invoice.TaxAmount,
            invoice.Currency,
            invoice.Shipment?.Reference,
            settings.LegalEntityName!,
            settings.RegisteredAddress!,
            settings.Gstin,
            bankDetails,
            settings.DeclarationText);

        var bytes = _pdfRenderer.Render(model);

        var relativePath = $"invoices/{invoice.Id}.pdf"; // TECH_SPEC §4.6's stated convention
        var storedPath = await _fileStorage.SaveAsync(relativePath, new MemoryStream(bytes), ct);
        invoice.PdfFilePath = storedPath;
    }

    // ---- Reference generation (M6 contract §1, reused verbatim from ShipmentService D-i) ---

    private async Task SaveWithGeneratedNumberAsync(Invoice invoice, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            invoice.InvoiceNumber = await GenerateInvoiceNumberAsync(ct);

            try
            {
                await _db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (UniqueViolationDetector.IsUniqueViolation(ex) && attempt < MaxReferenceAttempts)
            {
                // Entity stays Added after a failed SaveChanges; next iteration overwrites
                // InvoiceNumber and retries. See N-18 / ShipmentService.SaveWithGeneratedReferenceAsync.
            }
        }
    }

    private async Task<string> GenerateInvoiceNumberAsync(CancellationToken ct)
    {
        var prefix = await ResolvePrefixAsync(ct);
        var monthPrefix = $"{prefix}-{DateTime.UtcNow:yyMM}-";

        var existing = await _db.Invoices
            .Where(i => i.InvoiceNumber.StartsWith(monthPrefix))
            .Select(i => i.InvoiceNumber)
            .ToListAsync(ct);

        var maxSequence = existing
            .Select(number => int.TryParse(number[monthPrefix.Length..], out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{monthPrefix}{maxSequence + 1:D3}";
    }

    private async Task<string> ResolvePrefixAsync(CancellationToken ct)
    {
        var settings = await _db.CompanySettings.FindAsync([CompanySettings.SingletonId], ct);
        var configured = settings?.InvoiceNumberPrefix?.Trim();
        // M6 contract §1: invoice CREATION must work before company settings are configured —
        // only PDF rendering is gated. Defaults to "INV" when the row is absent or null.
        return string.IsNullOrWhiteSpace(configured) ? DefaultPrefix : configured.ToUpperInvariant();
    }

    // ---- Validation helpers ------------------------------------------------------------------

    private static void EnsureDraft(Invoice invoice)
    {
        if (!string.Equals(invoice.Status.Code, InvoiceStatusCodes.Draft, StringComparison.Ordinal))
        {
            throw new InvoiceConflictException(
                "Invoice is not editable.",
                $"This invoice is '{invoice.Status.Code}' and can only be edited while Draft.",
                new Dictionary<string, object?> { ["currentStatus"] = invoice.Status.Code });
        }
    }

    /// <summary>
    /// M6 contract §5. Branches on <c>Code</c> only (D-50 precedent). PAID and CANCELLED are
    /// terminal; DRAFT/ISSUED may move to CANCELLED; DRAFT may move to ISSUED; ISSUED may NOT
    /// move to PAID here — that is <see cref="MarkPaidAsync"/>'s job exclusively, so it is
    /// rejected the same as any other illegal move.
    /// </summary>
    private static void EnsureLegalTransition(string fromCode, string toCode)
    {
        if (string.Equals(fromCode, toCode, StringComparison.Ordinal))
        {
            ThrowInvalidTransition(fromCode, toCode);
        }

        var legal = (fromCode, toCode) switch
        {
            (InvoiceStatusCodes.Draft, InvoiceStatusCodes.Issued) => true,
            (InvoiceStatusCodes.Draft, InvoiceStatusCodes.Cancelled) => true,
            (InvoiceStatusCodes.Issued, InvoiceStatusCodes.Cancelled) => true,
            _ => false
        };

        if (!legal)
        {
            ThrowInvalidTransition(fromCode, toCode);
        }
    }

    private static void ThrowInvalidTransition(string fromCode, string toCode) =>
        throw new InvoiceConflictException(
            "Invalid invoice status transition.",
            $"Cannot move an invoice from '{fromCode}' to '{toCode}'.",
            new Dictionary<string, object?> { ["fromStatus"] = fromCode, ["toStatus"] = toCode });

    private static void ValidateAmounts(decimal amount, decimal taxAmount)
    {
        var errors = new Dictionary<string, string[]>();
        if (amount < 0)
        {
            errors["amount"] = ["Amount cannot be negative."];
        }
        if (taxAmount < 0)
        {
            errors["taxAmount"] = ["Tax amount cannot be negative."];
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }
    }

    private static string NormalizeCurrency(string? currency)
    {
        var trimmed = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (trimmed.Length != 3)
        {
            throw new AppValidationException("currency", "Currency must be a 3-letter ISO code (e.g. INR).");
        }

        return trimmed;
    }

    private async Task<Shipment?> ResolveShipmentAsync(Guid? shipmentId, Guid customerId, CancellationToken ct)
    {
        if (!shipmentId.HasValue)
        {
            return null;
        }

        var shipment = await _db.Shipments.FirstOrDefaultAsync(s => s.Id == shipmentId.Value, ct)
            ?? throw new AppValidationException("shipmentId", "Unknown shipment.");

        if (shipment.CustomerId != customerId)
        {
            throw new AppValidationException("shipmentId", "This shipment does not belong to the given customer.");
        }

        return shipment;
    }

    // ---- Loading / mapping --------------------------------------------------------------------

    private Task<Invoice?> LoadWithNavigationsAsync(Guid id, CancellationToken ct) =>
        _db.Invoices
            .Include(i => i.Customer).ThenInclude(c => c.ServiceType)
            .Include(i => i.Status)
            .Include(i => i.Shipment)
            .Include(i => i.CreatedBy)
            .Include(i => i.StatusHistory).ThenInclude(h => h.Status)
            .Include(i => i.StatusHistory).ThenInclude(h => h.ChangedBy)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>Business name where present, contact name otherwise — same fallback ShipmentService.ResolveCustomerName uses, so a row never renders blank.</summary>
    private static string ResolveCustomerName(Customer c) =>
        string.IsNullOrWhiteSpace(c.BusinessName) ? c.Name : c.BusinessName;

    private static StatusRefDto MapStatus(InvoiceStatus s) => new(s.Id, s.Code, s.Label);

    private static StatusRefDto MapServiceType(ServiceType s) => new(s.Id, s.Code, s.Label);

    /// <summary>
    /// <see cref="Invoice.Customer"/> is a guaranteed invariant here, never a defensive null
    /// check: every call site that reaches <see cref="MapListItem"/>/<see cref="MapDetail"/>
    /// has already loaded it — <c>ListAsync</c>'s query and <c>LoadWithNavigationsAsync</c>
    /// both <c>.Include(i =&gt; i.Customer).ThenInclude(c =&gt; c.ServiceType)</c>, and
    /// <c>CreateAsync</c>'s in-memory fallback (when the reload races a concurrent delete) sets
    /// <c>Customer</c> explicitly before ever calling this. An earlier version tested
    /// <c>i.Customer is null</c> for the ref name only and then dereferenced it unconditionally
    /// one line later for <c>ServiceType</c> — internally incoherent (same defect class as
    /// D-63), removed rather than left as misleading dead code.
    /// </summary>
    private static InvoiceListItemDto MapListItem(Invoice i) => new(
        i.Id, i.InvoiceNumber,
        new CustomerRefDto(i.CustomerId, ResolveCustomerName(i.Customer)),
        MapServiceType(i.Customer.ServiceType),
        MapStatus(i.Status),
        DateOnly.FromDateTime(i.InvoiceDate),
        i.Amount, i.TaxAmount, i.Amount + i.TaxAmount, i.Currency,
        i.ShipmentId, i.Shipment?.Reference,
        i.PdfFilePath != null,
        i.PaidAt.HasValue ? AsUtc(i.PaidAt.Value) : null);

    private static InvoiceDetailDto MapDetail(Invoice i)
    {
        var history = i.StatusHistory.OrderBy(h => h.ChangedAt).ToList();

        // Same guaranteed invariant as MapListItem — see its doc comment.
        return new InvoiceDetailDto(
            i.Id, i.InvoiceNumber,
            new CustomerRefDto(i.CustomerId, ResolveCustomerName(i.Customer)),
            MapServiceType(i.Customer.ServiceType),
            MapStatus(i.Status),
            DateOnly.FromDateTime(i.InvoiceDate),
            i.Amount, i.TaxAmount, i.Amount + i.TaxAmount, i.Currency,
            i.ShipmentId, i.Shipment?.Reference,
            i.PdfFilePath != null,
            i.PaidAt.HasValue ? AsUtc(i.PaidAt.Value) : null,
            i.LineDescription,
            i.CreatedByUserId, i.CreatedBy?.Name ?? "(unknown)", AsUtc(i.CreatedAt),
            i.PaidReference,
            history.Select(MapHistory).ToList());
    }

    private static InvoiceStatusHistoryDto MapHistory(InvoiceStatusHistory h) => new(
        h.Id, MapStatus(h.Status), h.ChangedByUserId, h.ChangedBy?.Name ?? "(unknown)", AsUtc(h.ChangedAt), h.Note);
}
