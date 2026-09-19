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
            .Select(g => new { StatusId = g.Key, Count = g.Count(), TotalAmount = g.Sum(i => i.Amount + i.TaxAmount) })
            .ToListAsync(ct);
        var countsById = counts.ToDictionary(c => c.StatusId, c => (c.Count, c.TotalAmount));

        var statuses = await _db.InvoiceStatuses.ToListAsync(ct);

        return statuses
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Code, StringComparer.Ordinal)
            .Select(s =>
            {
                var (count, totalAmount) = countsById.GetValueOrDefault(s.Id, (0, 0m));
                return new InvoiceStatusCountDto(s.Id, s.Code, s.Label, s.SortOrder, count, totalAmount);
            })
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

        var currency = NormalizeCurrency(request.Currency);

        var draftStatus = await _db.InvoiceStatuses.FirstOrDefaultAsync(s => s.Code == InvoiceStatusCodes.Draft, ct)
            ?? throw new AppValidationException("statusId", $"No '{InvoiceStatusCodes.Draft}' invoice status is configured.");

        var invoiceId = Guid.NewGuid();
        var lines = await BuildLinesAsync(request.Lines, invoiceId, ct);
        var sellerStateCode = await ResolveSellerStateCodeAsync(ct);

        var invoice = new Invoice
        {
            Id = invoiceId,
            CustomerId = customer.Id,
            Customer = customer,
            ShipmentId = shipment?.Id,
            Shipment = shipment,
            InvoiceDate = request.InvoiceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            LineDescription = Trim(request.LineDescription),
            Lines = lines,
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

        RecalculateTotals(invoice, lines, ProvisionalIntraState(customer, sellerStateCode));

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

        var currency = NormalizeCurrency(request.Currency);
        var lines = await BuildLinesAsync(request.Lines, invoice.Id, ct);
        var sellerStateCode = await ResolveSellerStateCodeAsync(ct);

        invoice.CustomerId = customer.Id;
        invoice.Customer = customer;
        invoice.ShipmentId = shipment?.Id;
        invoice.Shipment = shipment;
        invoice.InvoiceDate = request.InvoiceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        invoice.LineDescription = Trim(request.LineDescription);
        invoice.Currency = currency;

        // Replace wholesale rather than diffing: the request carries the complete intended set
        // of lines, and line ids are not client-owned, so there is no identity to preserve
        // across an edit.
        //
        // The old rows are deleted and the new ones added THROUGH THE DbSet, leaving the
        // tracked `invoice.Lines` navigation untouched until after the save. Mutating that
        // navigation here — Clear(), or reassigning it — makes the change tracker re-evaluate
        // the severed relationship and downgrade the already-Deleted rows to Modified, which
        // then fails as "Attempted to update or delete an entity that does not exist in the
        // store". Going through the DbSet states the intent once, unambiguously.
        _db.InvoiceLines.RemoveRange(invoice.Lines.ToList());
        _db.InvoiceLines.AddRange(lines);

        RecalculateTotals(invoice, lines, ProvisionalIntraState(customer, sellerStateCode));

        await _db.SaveChangesAsync(ct);

        // Safe only now: the deleted rows are Detached post-save, so swapping the navigation
        // can no longer resurrect them. MapDetail reads this.
        invoice.Lines = lines;

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
            // Fix place of supply and recompute the totals against it BEFORE rendering, so the
            // PDF, the stored columns and the tax summary all come from one determination. The
            // draft's totals were provisional (see RecalculateTotals) — this is the figure that
            // becomes binding.
            await ApplyPlaceOfSupplyAsync(invoice, ct);
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

    // ---- Place of supply, fixed at issue -------------------------------------------------

    /// <summary>
    /// Determines the tax treatment and snapshots it onto the invoice. Refuses — loudly, as a
    /// 400 — whenever the determination cannot be made honestly:
    /// <list type="bullet">
    ///   <item>no lines at all;</item>
    ///   <item>a line with no GST rate or no HSN/SAC code (a compliant tax invoice must state both);</item>
    ///   <item>a line priced at zero, which is almost always an unset selling price rather than a genuine giveaway;</item>
    ///   <item>no state code on the seller.</item>
    /// </list>
    /// A customer's GSTIN and GST state are optional. When the buyer has neither, the place of
    /// supply defaults to the seller's own state (intra-state, CGST + SGST) rather than blocking
    /// the invoice; the place of supply is printed on the invoice either way.
    /// </summary>
    private async Task ApplyPlaceOfSupplyAsync(Invoice invoice, CancellationToken ct)
    {
        if (invoice.Lines.Count == 0)
        {
            throw new AppValidationException("lines",
                "This invoice has no line items. Add at least one line before issuing it.");
        }

        var lineErrors = new List<string>();
        foreach (var line in invoice.Lines.OrderBy(l => l.SortOrder))
        {
            if (line.GstRate is null)
            {
                lineErrors.Add($"'{line.Description}' has no GST rate.");
            }

            if (string.IsNullOrWhiteSpace(line.HsnCode))
            {
                lineErrors.Add($"'{line.Description}' has no HSN/SAC code.");
            }

            if (line.UnitPrice <= 0)
            {
                lineErrors.Add($"'{line.Description}' has no unit price.");
            }
        }

        if (lineErrors.Count > 0)
        {
            throw new AppValidationException("lines",
                "Every line needs a unit price, an HSN/SAC code and a GST rate before the invoice can be issued. " +
                string.Join(" ", lineErrors) +
                " Enter them on the invoice line.");
        }

        var sellerStateCode = await ResolveSellerStateCodeAsync(ct);
        if (sellerStateCode is null)
        {
            throw new AppValidationException("companySettings",
                "Your own GST state is not set. A Super Admin must set the state (or a valid GSTIN) under " +
                "Admin > Company Settings before an invoice can be issued — it decides whether GST splits into " +
                "CGST + SGST or is charged as IGST.");
        }

        // Optional on the customer: with no state and no GSTIN, supply is treated as intra-state.
        var buyerStateCode = ResolvePlaceOfSupply(invoice.Customer) ?? sellerStateCode;

        var isIntraState = GstCalculator.IsIntraState(sellerStateCode, buyerStateCode);
        invoice.PlaceOfSupplyStateCode = buyerStateCode;
        invoice.IsIntraState = isIntraState;
        RecalculateTotals(invoice, invoice.Lines, isIntraState);
    }

    /// <summary>The seller's state, from CompanySettings' explicit field or its GSTIN prefix. Null when neither is usable.</summary>
    private async Task<string?> ResolveSellerStateCodeAsync(CancellationToken ct)
    {
        var settings = await _db.CompanySettings.FindAsync([CompanySettings.SingletonId], ct);
        if (settings is null)
        {
            return null;
        }

        return IndianStateCodes.IsValid(settings.StateCode) ? settings.StateCode : IndianStateCodes.FromGstin(settings.Gstin);
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

        // ApplyPlaceOfSupplyAsync ran immediately before this and is the only caller path, so
        // IsIntraState is set by the time we get here. Defaulting would silently print the
        // wrong tax heads, so this asserts rather than coalesces.
        var isIntraState = invoice.IsIntraState
            ?? throw new InvalidOperationException("Place of supply must be determined before rendering an invoice PDF.");

        var pdfLines = invoice.Lines
            .OrderBy(l => l.SortOrder)
            .Select(l =>
            {
                var rate = l.GstRate ?? 0m;
                var amounts = ComputeLine(l, isIntraState);
                return new InvoicePdfLine(
                    l.Description, l.HsnCode, l.Quantity, l.UnitPrice, rate,
                    amounts.TaxableValue, amounts.Cgst, amounts.Sgst, amounts.Igst, amounts.LineTotal,
                    amounts.Discount);
            })
            .ToList();

        var taxSummary = new InvoicePdfTaxSummary(
            invoice.PlaceOfSupplyStateCode,
            IndianStateCodes.NameFor(invoice.PlaceOfSupplyStateCode),
            isIntraState,
            pdfLines.Sum(l => l.TaxableValue),
            pdfLines.Sum(l => l.CgstAmount),
            pdfLines.Sum(l => l.SgstAmount),
            pdfLines.Sum(l => l.IgstAmount),
            pdfLines.Sum(l => l.CgstAmount + l.SgstAmount + l.IgstAmount),
            pdfLines.Sum(l => l.DiscountAmount));

        var model = new InvoicePdfModel(
            invoice.InvoiceNumber,
            DateOnly.FromDateTime(invoice.InvoiceDate),
            ResolveCustomerName(invoice.Customer),
            invoice.Customer.City,
            invoice.Customer.Region,
            invoice.Customer.Gstin,
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
            settings.DeclarationText,
            pdfLines,
            taxSummary);

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

    // ---- Line items, pricing and GST ----------------------------------------------------

    /// <summary>
    /// Turns the incoming line requests into entities, resolving each one's price, HSN and GST
    /// rate against its inventory item.
    ///
    /// PRECEDENCE per field: an explicit value on the request wins, otherwise the item's value
    /// is snapshotted, otherwise the field stays null. Nothing is invented — in particular the
    /// unit price NEVER falls back to <c>InventoryItem.UnitCost</c>. Cost and selling price are
    /// different figures (see the entity docs); quietly billing at cost would discard the entire
    /// margin, and would do it invisibly on a document that is binding once issued.
    ///
    /// Missing HSN/rate is NOT an error here. A draft is allowed to be incomplete — that is what
    /// draft means — and <see cref="EnsureIssuable"/> is the gate that refuses at issue time.
    /// </summary>
    private async Task<List<InvoiceLine>> BuildLinesAsync(IReadOnlyList<UpsertInvoiceLineRequest>? requests, Guid invoiceId, CancellationToken ct)
    {
        if (requests is null || requests.Count == 0)
        {
            throw new AppValidationException("lines", "An invoice needs at least one line.");
        }

        var itemIds = requests.Where(r => r.InventoryItemId.HasValue).Select(r => r.InventoryItemId!.Value).Distinct().ToList();
        var items = itemIds.Count == 0
            ? new Dictionary<Guid, InventoryItem>()
            : await _db.InventoryItems.Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        var errors = new Dictionary<string, string[]>();
        var lines = new List<InvoiceLine>(requests.Count);

        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            InventoryItem? item = null;

            if (request.InventoryItemId.HasValue && !items.TryGetValue(request.InventoryItemId.Value, out item))
            {
                errors[$"lines[{index}].inventoryItemId"] = ["Unknown inventory item."];
                continue;
            }

            var description = Trim(request.Description) ?? item?.Name;
            if (string.IsNullOrWhiteSpace(description))
            {
                errors[$"lines[{index}].description"] = ["A line needs a description."];
            }

            if (request.Quantity <= 0)
            {
                errors[$"lines[{index}].quantity"] = ["Quantity must be greater than zero."];
            }

            var unitPrice = request.UnitPrice ?? item?.SellingPrice;
            if (unitPrice is < 0)
            {
                errors[$"lines[{index}].unitPrice"] = ["Unit price cannot be negative."];
            }

            var gstRate = request.GstRate ?? item?.GstRate;
            if (gstRate is < 0 or > 100)
            {
                errors[$"lines[{index}].gstRate"] = ["GST rate must be between 0 and 100."];
            }

            var (discountType, discountValue) = ValidateDiscount(request, index, request.Quantity, unitPrice ?? 0m, errors);

            lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoiceId,
                InventoryItemId = request.InventoryItemId,
                Description = description ?? string.Empty,
                HsnCode = Trim(request.HsnCode) ?? item?.HsnCode,
                Quantity = request.Quantity,
                UnitPrice = unitPrice ?? 0m,
                GstRate = gstRate,
                DiscountType = discountType,
                DiscountValue = discountValue,
                SortOrder = index
            });
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }

        return lines;
    }

    /// <summary>
    /// Normalises a line's discount: a zero or missing value means "no discount" and is stored
    /// as null/null, so the screen and PDF don't show a 0% discount. A percent must be 0–100; a
    /// flat amount can't exceed the line's gross value (that would make the taxable value
    /// negative). Errors are collected, not thrown, like the rest of the line checks.
    /// </summary>
    private static (string? Type, decimal? Value) ValidateDiscount(
        UpsertInvoiceLineRequest request, int index, decimal quantity, decimal unitPrice, Dictionary<string, string[]> errors)
    {
        var type = Trim(request.DiscountType)?.ToUpperInvariant();
        var value = request.DiscountValue ?? 0m;

        if (type is null || value == 0m)
        {
            return (null, null);
        }

        var key = $"lines[{index}].discountValue";
        if (type is not (InvoiceDiscountTypes.Percent or InvoiceDiscountTypes.Amount))
        {
            errors[$"lines[{index}].discountType"] = ["Discount type must be PERCENT or AMOUNT."];
        }
        else if (value < 0)
        {
            errors[key] = ["Discount cannot be negative."];
        }
        else if (type == InvoiceDiscountTypes.Percent && value > 100)
        {
            errors[key] = ["A percentage discount cannot exceed 100%."];
        }
        else if (type == InvoiceDiscountTypes.Amount && quantity > 0 && unitPrice >= 0
                 && value > Math.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero))
        {
            errors[key] = ["The discount cannot be more than the line's value."];
        }

        return (type, value);
    }

    /// <summary>
    /// A line's amounts with its discount applied. The single path every screen, PDF and total
    /// goes through, so the discount can never be applied in one place and missed in another.
    /// </summary>
    private static GstLineAmounts ComputeLine(InvoiceLine l, bool isIntraState) =>
        GstCalculator.ForLine(
            l.Quantity, l.UnitPrice, l.GstRate ?? 0m, isIntraState,
            GstCalculator.LineDiscount(l.Quantity, l.UnitPrice, l.DiscountType, l.DiscountValue));

    /// <summary>
    /// Recomputes <see cref="Invoice.Amount"/> and <see cref="Invoice.TaxAmount"/> from the
    /// lines. The ONLY writer of those two columns — see their doc comments for why they stay
    /// stored rather than becoming computed properties.
    ///
    /// <paramref name="isIntraState"/> matters even though the total might look split-agnostic:
    /// it is not. Intra-state rounds each half-rate head to 2dp and doubles it, which can differ
    /// from rounding the full rate once — 100.10 at 5% is 5.00 split (2.50 x 2) but 5.01 whole.
    /// A draft therefore carries a PROVISIONAL total computed from the customer's current state,
    /// and <see cref="ChangeStatusAsync"/> recomputes it against the snapshotted place of supply
    /// at issue, which is the figure that becomes binding.
    /// </summary>
    private static void RecalculateTotals(Invoice invoice, IEnumerable<InvoiceLine> lines, bool isIntraState)
    {
        var taxable = 0m;
        var tax = 0m;

        foreach (var line in lines)
        {
            var amounts = ComputeLine(line, isIntraState);
            taxable += amounts.TaxableValue;
            tax += amounts.TotalTax;
        }

        invoice.Amount = taxable;
        invoice.TaxAmount = tax;
    }

    /// <summary>
    /// The buyer's place of supply, defaulted from their GSTIN when the explicit field is blank.
    /// Null when neither is usable; callers then fall back to the seller's own state.
    /// </summary>
    private static string? ResolvePlaceOfSupply(Customer customer) =>
        IndianStateCodes.IsValid(customer.StateCode) ? customer.StateCode : IndianStateCodes.FromGstin(customer.Gstin);

    /// <summary>
    /// The DRAFT-only provisional treatment, using the same fallback the issue step applies: a
    /// customer with no usable state code is treated as being in the seller's own state.
    /// </summary>
    private static bool ProvisionalIntraState(Customer customer, string? sellerStateCode)
    {
        if (sellerStateCode is null) return false;
        var buyer = ResolvePlaceOfSupply(customer) ?? sellerStateCode;
        return GstCalculator.IsIntraState(sellerStateCode, buyer);
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
            .Include(i => i.Lines)
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
        var lines = i.Lines.OrderBy(l => l.SortOrder).ToList();
        // Issued invoices render against their snapshot; drafts against a provisional
        // inter-state reading — see MapLine.
        var isIntraState = i.IsIntraState ?? false;

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
            history.Select(MapHistory).ToList(),
            lines.Select(l => MapLine(l, isIntraState)).ToList(),
            BuildTaxSummary(i, lines, isIntraState));
    }

    /// <summary>
    /// A DRAFT has no fixed place of supply yet, so its lines are shown against a PROVISIONAL
    /// treatment rather than a settled one — the invoice's own snapshot once issued, falling
    /// back to inter-state while it is still a draft. The DTO carries
    /// <c>TaxSummary.IsIntraState</c> as null in that case so the client can say "provisional"
    /// instead of presenting a guess as settled.
    /// </summary>
    private static InvoiceLineDto MapLine(InvoiceLine l, bool isIntraState)
    {
        var amounts = ComputeLine(l, isIntraState);
        return new InvoiceLineDto(
            l.Id, l.InventoryItemId, l.Description, l.HsnCode,
            l.Quantity, l.UnitPrice, l.GstRate,
            amounts.TaxableValue, amounts.Cgst, amounts.Sgst, amounts.Igst, amounts.LineTotal,
            l.SortOrder,
            amounts.GrossValue, l.DiscountType, l.DiscountValue, amounts.Discount);
    }

    private static InvoiceTaxSummaryDto BuildTaxSummary(Invoice i, List<InvoiceLine> lines, bool isIntraState)
    {
        var computed = lines
            .Select(l => (Rate: l.GstRate ?? 0m, Amounts: ComputeLine(l, isIntraState)))
            .ToList();

        var breakdown = computed
            .GroupBy(c => c.Rate)
            .OrderBy(g => g.Key)
            .Select(g => new InvoiceTaxRateBreakdownDto(
                g.Key,
                g.Sum(c => c.Amounts.TaxableValue),
                g.Sum(c => c.Amounts.Cgst),
                g.Sum(c => c.Amounts.Sgst),
                g.Sum(c => c.Amounts.Igst)))
            .ToList();

        return new InvoiceTaxSummaryDto(
            i.PlaceOfSupplyStateCode,
            IndianStateCodes.NameFor(i.PlaceOfSupplyStateCode),
            i.IsIntraState,
            computed.Sum(c => c.Amounts.TaxableValue),
            computed.Sum(c => c.Amounts.Cgst),
            computed.Sum(c => c.Amounts.Sgst),
            computed.Sum(c => c.Amounts.Igst),
            computed.Sum(c => c.Amounts.TotalTax),
            breakdown,
            computed.Sum(c => c.Amounts.GrossValue),
            computed.Sum(c => c.Amounts.Discount));
    }

    private static InvoiceStatusHistoryDto MapHistory(InvoiceStatusHistory h) => new(
        h.Id, MapStatus(h.Status), h.ChangedByUserId, h.ChangedBy?.Name ?? "(unknown)", AsUtc(h.ChangedAt), h.Note);
}
