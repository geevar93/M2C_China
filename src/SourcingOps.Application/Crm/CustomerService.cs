using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Constants;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Crm;

/// <summary>
/// Implements ACTION_PLAN E4-01…E4-12. Status changes and owner reassignments are recorded as
/// system-generated <see cref="Interaction"/> rows (Type = one of <see cref="InteractionSystemTypes"/>)
/// rather than a separate history table — <see cref="Interaction.Type"/>'s own doc comment in
/// `Crm.cs` already anticipated "StatusChange" as an example value, and this is what lets
/// <see cref="GetTimelineAsync"/> serve E4-07's "one chronological feed" from a single table
/// today, with `CatalogDispatched`/`ShipmentRecorded`/`InvoiceCreated`/`InvoiceStatusChanged`
/// slotting in later (M4/M5/M6) without changing <see cref="TimelineEventDto"/>'s shape.
///
/// List queries materialize the current page (with `Include`) and map client-side rather than
/// projecting inside the LINQ expression tree — same defensive choice
/// <see cref="MasterData.MasterDataService"/> already documents, so the exact same query shape
/// is correct against both the real Npgsql provider and the EF Core InMemory provider used by
/// the Application-layer unit tests.
/// </summary>
public sealed class CustomerService : ICustomerService
{
    /// <summary>
    /// Reserved <see cref="Interaction.Type"/> values used only for system-generated timeline
    /// rows. <c>POST /customers/{id}/interactions</c> (E4-07) rejects a caller attempting to
    /// set one of these directly — that would let a client fabricate a fake status/owner
    /// change entry with no corresponding real change or audit-logged action behind it.
    /// </summary>
    private static class InteractionSystemTypes
    {
        public const string EnquiryCaptured = "EnquiryCaptured";
        public const string StatusChange = "StatusChange";
        public const string OwnerChanged = "OwnerChanged";

        public static readonly string[] Reserved = [EnquiryCaptured, StatusChange, OwnerChanged];
    }

    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly CustomerOptions _options;

    public CustomerService(IAppDbContext db, IAuditLogger audit, CustomerOptions options)
    {
        _db = db;
        _audit = audit;
        _options = options;
    }

    // ---- List / search / filter (E4-06) --------------------------------------------

    public async Task<CustomerListResultDto> ListAsync(CustomerListQuery query, CancellationToken ct = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 200 ? 25 : query.PageSize;

        var filtered = _db.Customers.AsQueryable();

        if (query.StatusId.HasValue)
        {
            filtered = filtered.Where(c => c.StatusId == query.StatusId.Value);
        }
        if (query.ServiceTypeId.HasValue)
        {
            filtered = filtered.Where(c => c.ServiceTypeId == query.ServiceTypeId.Value);
        }
        if (query.CategoryId.HasValue)
        {
            filtered = filtered.Where(c => c.CustomerCategories.Any(cc => cc.CategoryId == query.CategoryId.Value));
        }
        if (!string.IsNullOrWhiteSpace(query.Region))
        {
            filtered = filtered.Where(c => c.Region == query.Region);
        }
        if (query.OwnerUserId.HasValue)
        {
            filtered = filtered.Where(c => c.OwnerUserId == query.OwnerUserId.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            filtered = filtered.Where(c => c.Tags.Contains(query.Tag));
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // .ToLower().Contains(...) rather than EF.Functions.ILike — must translate
            // identically against both the real Npgsql provider and the EF Core InMemory
            // provider used by the Application-layer unit tests (same reasoning as
            // AdminUserService.ListAsync).
            var term = query.Search.Trim().ToLowerInvariant();
            filtered = filtered.Where(c =>
                c.Name.ToLower().Contains(term) ||
                c.Phone.ToLower().Contains(term) ||
                (c.BusinessName != null && c.BusinessName.ToLower().Contains(term)));
        }

        var totalCount = await filtered.CountAsync(ct);

        var pageEntities = await filtered
            .Include(c => c.Owner)
            .Include(c => c.CustomerCategories)
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = pageEntities.Select(MapListItem).ToList();
        return new CustomerListResultDto(items, page, pageSize, totalCount);
    }

    // ---- Get one -------------------------------------------------------------------

    public async Task<CustomerDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await LoadWithNavigationsAsync(id, ct);
        return customer is null ? null : MapDetail(customer);
    }

    // ---- Create (E4-01…E4-05, E4-10, E4-12) -----------------------------------------

    public async Task<CreateCustomerOutcome> CreateAsync(CreateCustomerRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppValidationException("name", "Name is required.");
        }

        var normalizedPhone = PhoneNumberNormalizer.Normalize(request.Phone, _options.DefaultCountryPhoneCode);

        var serviceType = await _db.ServiceTypes.FindAsync([request.ServiceTypeId], ct)
            ?? throw new AppValidationException("serviceTypeId", "Unknown service type.");

        var status = await ResolveStatusForCreateAsync(request.StatusId, ct);

        var isFreightOnly = IsFreightOnly(serviceType);
        ValidateExternalPurchaseFields(isFreightOnly, request.ExternalMarketplace, request.ExternalOrderRef,
            request.ExternalSupplierName, request.ExternalOrderValue, request.ExternalOrderCurrency, request.ExternalOrderDate);

        var categoryIds = await ResolveCategoryIdsAsync(request.CategoryIds, ct);
        var owner = await ResolveOwnerAsync(request.OwnerUserId, ct);
        var gstin = NormalizeAndValidateGstin(request.Gstin);

        if (!request.ConfirmDuplicate)
        {
            var existing = await _db.Customers
                .Include(c => c.Owner)
                .Include(c => c.CustomerCategories)
                .FirstOrDefaultAsync(c => c.Phone == normalizedPhone, ct);
            if (existing is not null)
            {
                return CreateCustomerOutcome.Duplicate(MapListItem(existing));
            }
        }

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Name = name,
            BusinessName = Trim(request.BusinessName),
            Phone = normalizedPhone,
            Email = Trim(request.Email),
            Gstin = gstin,
            StateCode = NormalizeAndValidateStateCode(request.StateCode, gstin),
            City = Trim(request.City),
            Region = Trim(request.Region),
            SourceChannel = Trim(request.SourceChannel),
            ServiceTypeId = serviceType.Id,
            ServiceType = serviceType,
            StatusId = status.Id,
            Status = status,
            OwnerUserId = owner?.Id,
            Owner = owner,
            Tags = NormalizeTags(request.Tags),
            Notes = Trim(request.Notes),
            ExternalMarketplace = Trim(request.ExternalMarketplace),
            ExternalOrderRef = Trim(request.ExternalOrderRef),
            ExternalSupplierName = Trim(request.ExternalSupplierName),
            ExternalOrderValue = request.ExternalOrderValue,
            ExternalOrderCurrency = Trim(request.ExternalOrderCurrency)?.ToUpperInvariant(),
            ExternalOrderDate = request.ExternalOrderDate,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var categoryId in categoryIds)
        {
            customer.CustomerCategories.Add(new CustomerCategory { CustomerId = customer.Id, CategoryId = categoryId });
        }

        _db.Customers.Add(customer);

        _db.Interactions.Add(new Interaction
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            AuthorUserId = actorUserId,
            Type = InteractionSystemTypes.EnquiryCaptured,
            Text = BuildEnquiryCapturedText(serviceType.Label, customer.SourceChannel),
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "CustomerCreated", "Customer", customer.Id.ToString(),
            new { customer.Name, customer.Phone, ServiceType = serviceType.Code }, ct);

        return CreateCustomerOutcome.Success(MapDetail(customer));
    }

    // ---- Update (general edit; owner is NOT touched here — see ChangeOwnerAsync) ---

    public async Task<CustomerDetailDto?> UpdateAsync(Guid id, UpdateCustomerRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var customer = await LoadWithNavigationsAsync(id, ct);
        if (customer is null)
        {
            return null;
        }

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppValidationException("name", "Name is required.");
        }

        var normalizedPhone = PhoneNumberNormalizer.Normalize(request.Phone, _options.DefaultCountryPhoneCode);

        var serviceType = await _db.ServiceTypes.FindAsync([request.ServiceTypeId], ct)
            ?? throw new AppValidationException("serviceTypeId", "Unknown service type.");
        var status = await _db.LeadStatuses.FindAsync([request.StatusId], ct)
            ?? throw new AppValidationException("statusId", "Unknown lead status.");

        var isFreightOnly = IsFreightOnly(serviceType);
        ValidateExternalPurchaseFields(isFreightOnly, request.ExternalMarketplace, request.ExternalOrderRef,
            request.ExternalSupplierName, request.ExternalOrderValue, request.ExternalOrderCurrency, request.ExternalOrderDate);

        var categoryIds = await ResolveCategoryIdsAsync(request.CategoryIds, ct);
        var gstin = NormalizeAndValidateGstin(request.Gstin);

        var statusChanged = customer.StatusId != status.Id;
        var previousStatusLabel = statusChanged ? customer.Status?.Label ?? (await _db.LeadStatuses.FindAsync([customer.StatusId], ct))?.Label : null;

        customer.Name = name;
        customer.BusinessName = Trim(request.BusinessName);
        customer.Phone = normalizedPhone;
        customer.Email = Trim(request.Email);
        customer.Gstin = gstin;
        customer.StateCode = NormalizeAndValidateStateCode(request.StateCode, gstin);
        customer.City = Trim(request.City);
        customer.Region = Trim(request.Region);
        customer.SourceChannel = Trim(request.SourceChannel);
        customer.ServiceTypeId = serviceType.Id;
        customer.StatusId = status.Id;
        customer.Tags = NormalizeTags(request.Tags);
        customer.Notes = Trim(request.Notes);
        customer.ExternalMarketplace = Trim(request.ExternalMarketplace);
        customer.ExternalOrderRef = Trim(request.ExternalOrderRef);
        customer.ExternalSupplierName = Trim(request.ExternalSupplierName);
        customer.ExternalOrderValue = request.ExternalOrderValue;
        customer.ExternalOrderCurrency = Trim(request.ExternalOrderCurrency)?.ToUpperInvariant();
        customer.ExternalOrderDate = request.ExternalOrderDate;

        foreach (var link in customer.CustomerCategories.ToList())
        {
            _db.CustomerCategories.Remove(link);
        }
        customer.CustomerCategories.Clear();
        foreach (var categoryId in categoryIds)
        {
            customer.CustomerCategories.Add(new CustomerCategory { CustomerId = customer.Id, CategoryId = categoryId });
        }

        if (statusChanged)
        {
            _db.Interactions.Add(new Interaction
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                AuthorUserId = actorUserId,
                Type = InteractionSystemTypes.StatusChange,
                Text = $"Status changed from {previousStatusLabel ?? "(unknown)"} to {status.Label}.",
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, "CustomerUpdated", "Customer", customer.Id.ToString(), new { customer.Name, customer.Phone }, ct);

        return MapDetail(customer);
    }

    // ---- Timeline (E4-07) -----------------------------------------------------------

    public async Task<IReadOnlyList<TimelineEventDto>?> GetTimelineAsync(Guid id, CancellationToken ct = default)
    {
        var exists = await _db.Customers.AnyAsync(c => c.Id == id, ct);
        if (!exists)
        {
            return null;
        }

        // Deliberately NOT ordered here — interactions and dispatches are two independent
        // sources that both need to land in ONE true chronological order. Sorting either
        // source before the union (the naive approach) only happens to look right when a
        // dispatch lands at either end of the feed; it silently produces the wrong order the
        // moment a dispatch's SentAt falls strictly between two interactions' CreatedAt
        // values. The single OrderByDescending below, applied to the merged list, is what
        // actually guarantees correctness regardless of how the two source timestamps
        // interleave.
        var interactions = await _db.Interactions
            .Include(i => i.Author)
            .Where(i => i.CustomerId == id)
            .ToListAsync(ct);

        // M4/E9: CatalogDispatched is now a live source, read (not duplicated) from the
        // `dispatches` log — a dispatch is never also written as an Interaction row, so
        // there is exactly one source of truth and a deleted dispatch would disappear from
        // the timeline rather than leaving an orphaned entry behind. CatalogDocument +
        // CatalogSection (or, since M6/E8-06, Invoice) are pulled in the same query (one round
        // trip, no N+1) purely to render the sent item's name into the event text below.
        var dispatches = await _db.Dispatches
            // CatalogDocument is nullable since M6/E8-06 (an invoice-targeted dispatch has
            // none) — EF translates this to a LEFT JOIN and handles the null row fine at
            // runtime; the null-forgiving operator here is just telling the compiler that,
            // not asserting it never happens (MapDispatchTimelineEvent branches on
            // CatalogDocumentId precisely because it CAN be null).
            .Include(d => d.CatalogDocument!).ThenInclude(doc => doc.CatalogSection)
            .Include(d => d.Invoice)
            .Include(d => d.StaffUser)
            .Where(d => d.CustomerId == id)
            .ToListAsync(ct);

        // M6 pass: ShipmentRecorded fills the gap left open at M5 close-out — a shipment now
        // has data to read (§16.6/N-18's sibling gap, not a new one). Read-only from
        // `shipments`, same rule as CatalogDispatched above. StatusHistory is pulled so the
        // event can name whoever recorded the shipment, matching ShipmentDetailDto.RecordedByName's
        // own "earliest history row's actor" convention.
        var shipments = await _db.Shipments
            .Include(s => s.StatusHistory).ThenInclude(h => h.ChangedBy)
            .Where(s => s.CustomerId == id)
            .ToListAsync(ct);

        // M6/E8-04: InvoiceCreated and InvoiceStatusChanged are both live sources, read from
        // `invoices`/`invoice_status_history` — same read-don't-duplicate rule. The FIRST
        // status-history row (written by InvoiceService.CreateAsync at creation time, always
        // Draft) is skipped when producing InvoiceStatusChanged events: it is the same moment
        // InvoiceCreated already reports, and surfacing both would double up one real event.
        var invoices = await _db.Invoices
            .Include(i => i.CreatedBy)
            .Include(i => i.StatusHistory).ThenInclude(h => h.Status)
            .Include(i => i.StatusHistory).ThenInclude(h => h.ChangedBy)
            .Where(i => i.CustomerId == id)
            .ToListAsync(ct);

        return interactions.Select(MapTimelineEvent)
            .Concat(dispatches.Select(MapDispatchTimelineEvent))
            .Concat(shipments.Select(MapShipmentTimelineEvent))
            .Concat(invoices.Select(MapInvoiceCreatedTimelineEvent))
            .Concat(invoices.SelectMany(MapInvoiceStatusChangedTimelineEvents))
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToList();
    }

    // ---- Interactions / notes (E4-07) ------------------------------------------------

    public async Task<InteractionDto?> AddInteractionAsync(Guid id, CreateInteractionRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null)
        {
            return null;
        }

        var text = (request.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AppValidationException("text", "Interaction text is required.");
        }

        var type = string.IsNullOrWhiteSpace(request.Type) ? "Note" : request.Type.Trim();
        if (InteractionSystemTypes.Reserved.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            throw new AppValidationException("type", $"'{type}' is a reserved interaction type and cannot be set directly.");
        }

        var author = await _db.Users.FindAsync([actorUserId], ct);

        var interaction = new Interaction
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            AuthorUserId = actorUserId,
            Type = type,
            Text = text,
            FollowUpDate = request.FollowUpDate,
            CreatedAt = DateTime.UtcNow
        };
        _db.Interactions.Add(interaction);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "InteractionAdded", "Customer", customer.Id.ToString(),
            new { interaction.Type, HasFollowUp = interaction.FollowUpDate.HasValue }, ct);

        return new InteractionDto(
            interaction.Id, interaction.CustomerId, interaction.Type, interaction.Text,
            AsUtcOrNull(interaction.FollowUpDate), interaction.AuthorUserId, author?.Name ?? "(unknown)",
            AsUtc(interaction.CreatedAt));
    }

    // ---- Owner assignment (E4-09) ---------------------------------------------------

    public async Task<CustomerDetailDto?> ChangeOwnerAsync(Guid id, Guid? ownerUserId, Guid actorUserId, CancellationToken ct = default)
    {
        var customer = await LoadWithNavigationsAsync(id, ct);
        if (customer is null)
        {
            return null;
        }

        var newOwner = await ResolveOwnerAsync(ownerUserId, ct);

        if (customer.OwnerUserId == newOwner?.Id)
        {
            return MapDetail(customer); // idempotent no-op
        }

        var previousOwnerName = customer.Owner?.Name ?? "(unassigned)";
        var newOwnerName = newOwner?.Name ?? "(unassigned)";

        customer.OwnerUserId = newOwner?.Id;
        customer.Owner = newOwner;

        _db.Interactions.Add(new Interaction
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            AuthorUserId = actorUserId,
            Type = InteractionSystemTypes.OwnerChanged,
            Text = $"Owner changed from {previousOwnerName} to {newOwnerName}.",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(actorUserId, "CustomerOwnerChanged", "Customer", customer.Id.ToString(),
            new { PreviousOwner = previousOwnerName, NewOwner = newOwnerName }, ct);

        return MapDetail(customer);
    }

    // ---- Due follow-ups (E4-08) ------------------------------------------------------

    public async Task<IReadOnlyList<DueFollowUpDto>> GetDueFollowUpsAsync(DateTime asOfUtc, CancellationToken ct = default)
    {
        // No "resolved"/"dismissed" flag exists on Interaction (schema has only
        // follow_up_date) — "due" is therefore simply follow_up_date <= asOf, and a reminder
        // stays "due" indefinitely once its date passes unless superseded by a new
        // interaction. Flagged as an open item in the build report; adding a resolution flag
        // is a follow-up schema decision, not silently assumed here.
        var due = await _db.Interactions
            .Include(i => i.Customer)
            .Include(i => i.Author)
            .Where(i => i.FollowUpDate != null && i.FollowUpDate <= asOfUtc)
            .OrderBy(i => i.FollowUpDate)
            .ToListAsync(ct);

        return due
            .Select(i => new DueFollowUpDto(i.Id, i.CustomerId, i.Customer.Name, i.Text, AsUtc(i.FollowUpDate!.Value), i.Author?.Name ?? "(unknown)"))
            .ToList();
    }

    // ---- Shared helpers ---------------------------------------------------------------

    private Task<Customer?> LoadWithNavigationsAsync(Guid id, CancellationToken ct) =>
        _db.Customers.Include(c => c.Owner).Include(c => c.CustomerCategories).Include(c => c.Status)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    private async Task<LeadStatus> ResolveStatusForCreateAsync(Guid? requestedStatusId, CancellationToken ct)
    {
        if (requestedStatusId.HasValue)
        {
            return await _db.LeadStatuses.FindAsync([requestedStatusId.Value], ct)
                ?? throw new AppValidationException("statusId", "Unknown lead status.");
        }

        // FR-CRM-05's pipeline implies a default entry point when intake doesn't specify one
        // explicitly — the lowest-sort-order ACTIVE lead status (seeded as "New", E1-03).
        // Flagged as an assumption: the FSD/TECH_SPEC binding contract does not spell out
        // whether statusId is mandatory on create.
        return await _db.LeadStatuses.Where(s => s.IsActive).OrderBy(s => s.SortOrder).FirstOrDefaultAsync(ct)
            ?? throw new AppValidationException("statusId", "No active lead status is configured; specify statusId explicitly.");
    }

    private async Task<List<Guid>> ResolveCategoryIdsAsync(IReadOnlyList<Guid>? requested, CancellationToken ct)
    {
        if (requested is null || requested.Count == 0)
        {
            return [];
        }

        var distinct = requested.Distinct().ToList();
        var existingIds = await _db.Categories.Where(c => distinct.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
        if (existingIds.Count != distinct.Count)
        {
            var existingSet = existingIds.ToHashSet();
            var unknown = distinct.Where(id => !existingSet.Contains(id));
            throw new AppValidationException("categoryIds", $"Unknown category id(s): {string.Join(", ", unknown)}.");
        }

        return distinct;
    }

    private async Task<User?> ResolveOwnerAsync(Guid? ownerUserId, CancellationToken ct)
    {
        if (!ownerUserId.HasValue)
        {
            return null;
        }

        var user = await _db.Users.FindAsync([ownerUserId.Value], ct);
        if (user is null || !user.IsActive)
        {
            throw new AppValidationException("ownerUserId", "Owner must reference an active user.");
        }

        return user;
    }

    private static bool IsFreightOnly(ServiceType serviceType) =>
        string.Equals(serviceType.Code, SeedDefaults.ServiceTypeFreightOnly, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// E4-12: the six <c>external_*</c> columns are only meaningful for freight-only
    /// customers — validated rather than silently accepted (the coordinator's explicit
    /// instruction), so bad data cannot accumulate invisibly on a CIF customer.
    /// </summary>
    private static void ValidateExternalPurchaseFields(bool isFreightOnly, string? marketplace, string? orderRef,
        string? supplierName, decimal? orderValue, string? orderCurrency, DateTime? orderDate)
    {
        var anyPopulated = !string.IsNullOrWhiteSpace(marketplace) || !string.IsNullOrWhiteSpace(orderRef) ||
            !string.IsNullOrWhiteSpace(supplierName) || orderValue.HasValue || !string.IsNullOrWhiteSpace(orderCurrency) || orderDate.HasValue;

        if (anyPopulated && !isFreightOnly)
        {
            throw new AppValidationException("externalMarketplace",
                "External-purchase fields (marketplace/order ref/supplier/value/currency/date) are only valid for freight-only customers.");
        }
    }

    private static string BuildEnquiryCapturedText(string serviceTypeLabel, string? sourceChannel) =>
        string.IsNullOrWhiteSpace(sourceChannel)
            ? $"New {serviceTypeLabel} enquiry captured."
            : $"New {serviceTypeLabel} enquiry captured via {sourceChannel}.";

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// N-37: the buyer's GSTIN, optional (see <see cref="Customer.Gstin"/>'s doc comment).
    /// Blank/whitespace trims to null — "cleared" and "never set" are the same state, matching
    /// how <c>CompanySettingsService.UpsertAsync</c> already treats every other trimmed field
    /// (D-70 precedent). Stored uppercased since GSTINs are canonically uppercase and mixed
    /// case would make stored values compare/display inconsistently.
    ///
    /// Validation is deliberately shallow: exactly 15 alphanumeric characters, nothing more.
    /// No checksum digit validation and no full structural regex (state-code prefix, PAN
    /// segment, entity-code digit, etc.) — a wrongly-rejected real GSTIN is a worse failure
    /// here than a wrongly-accepted malformed one, and the human entering it knows their
    /// customer's number better than this service does. Do not "improve" this into a stricter
    /// pattern without a product decision behind it.
    /// </summary>
    private static string? NormalizeAndValidateGstin(string? value)
    {
        var trimmed = Trim(value)?.ToUpperInvariant();
        if (trimmed is null)
        {
            return null;
        }

        if (trimmed.Length != 15 || !trimmed.All(char.IsLetterOrDigit))
        {
            throw new AppValidationException("gstin", "GSTIN must be exactly 15 alphanumeric characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// The buyer's place-of-supply state code. Blank falls back to the GSTIN's first two
    /// digits, which ARE the state code — a derivation, not a guess — so a correctly entered
    /// GSTIN normally means nobody has to pick a state by hand.
    ///
    /// An explicitly supplied code must be a real one. Rejecting an unknown code matters more
    /// here than it would for a display field: this value decides CGST+SGST versus IGST, and a
    /// junk code would silently be treated as "different from the seller's" and charge IGST on
    /// what might be a local supply.
    /// </summary>
    private static string? NormalizeAndValidateStateCode(string? value, string? gstin)
    {
        var trimmed = Trim(value);
        if (trimmed is null)
        {
            return IndianStateCodes.FromGstin(gstin);
        }

        if (!IndianStateCodes.IsValid(trimmed))
        {
            throw new AppValidationException("stateCode", $"'{trimmed}' is not a valid Indian GST state code.");
        }

        return trimmed;
    }

    private static string[] NormalizeTags(IReadOnlyList<string>? tags) =>
        (tags ?? []).Select(t => t?.Trim() ?? string.Empty).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtcOrNull(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;

    private static CustomerListItemDto MapListItem(Customer c) => new(
        c.Id, c.Name, c.BusinessName, c.Phone, c.City, c.Region, c.SourceChannel,
        c.ServiceTypeId, c.StatusId,
        c.CustomerCategories.Select(cc => cc.CategoryId).ToList(),
        c.OwnerUserId, c.Owner?.Name,
        c.Tags.ToList(), AsUtc(c.CreatedAt));

    private static CustomerDetailDto MapDetail(Customer c) => new(
        c.Id, c.Name, c.BusinessName, c.Phone, c.City, c.Region, c.SourceChannel,
        c.ServiceTypeId, c.StatusId,
        c.CustomerCategories.Select(cc => cc.CategoryId).ToList(),
        c.OwnerUserId, c.Owner?.Name,
        c.Tags.ToList(), AsUtc(c.CreatedAt),
        c.Email, c.Notes,
        c.ExternalMarketplace, c.ExternalOrderRef, c.ExternalSupplierName,
        c.ExternalOrderValue, c.ExternalOrderCurrency, AsUtcOrNull(c.ExternalOrderDate),
        c.Gstin,
        c.StateCode,
        IndianStateCodes.NameFor(c.StateCode));

    private static TimelineEventDto MapTimelineEvent(Interaction i)
    {
        var kind = i.Type switch
        {
            InteractionSystemTypes.EnquiryCaptured => TimelineEventKinds.EnquiryCaptured,
            InteractionSystemTypes.StatusChange => TimelineEventKinds.StatusChanged,
            InteractionSystemTypes.OwnerChanged => TimelineEventKinds.OwnerChanged,
            _ => TimelineEventKinds.NoteAdded
        };

        var title = kind switch
        {
            TimelineEventKinds.EnquiryCaptured => "Lead captured",
            TimelineEventKinds.StatusChanged => "Status changed",
            TimelineEventKinds.OwnerChanged => "Owner changed",
            _ => string.Equals(i.Type, "Note", StringComparison.OrdinalIgnoreCase) ? "Note added" : $"{i.Type} logged"
        };

        // refType/refId stay null for every interaction-sourced kind — there is no
        // "Interaction" value in the RefType vocabulary the coordinator fixed
        // (Shipment | CatalogDocument | Invoice | null); those are reserved for kinds sourced
        // from other tables (see MapDispatchTimelineEvent below for the first one M4 populates).
        return new TimelineEventDto(kind, AsUtc(i.CreatedAt), title, i.Text, i.AuthorUserId, i.Author?.Name, null, null);
    }

    /// <summary>
    /// M4/E9: the first live <see cref="TimelineEventKinds.CatalogDispatched"/> event —
    /// derived read-only from a <c>dispatches</c> row, never a duplicated <c>Interaction</c>
    /// write (see <see cref="GetTimelineAsync"/>'s doc comment). <see cref="TimelineEventDto.RefType"/>
    /// is "CatalogDocument" (not "Dispatch") and <see cref="TimelineEventDto.RefId"/> is the
    /// catalog document id — deliberately, because the client already has a real, navigable
    /// destination for a catalog document (<c>GET /catalog-documents/{id}/download</c>); there
    /// is no equivalent "open this dispatch" destination, so pointing at the dispatch row
    /// itself would be a dead reference. The event text names the catalog and the document by
    /// itself, so the client never needs a second round trip to render something meaningful.
    /// </summary>
    private static TimelineEventDto MapDispatchTimelineEvent(Dispatch d)
    {
        if (d.CatalogDocumentId.HasValue)
        {
            var catalogName = d.CatalogDocument?.CatalogSection?.Title ?? "(unknown catalog)";
            var documentName = d.CatalogDocument?.OriginalFilename ?? "(unknown document)";

            return new TimelineEventDto(
                TimelineEventKinds.CatalogDispatched,
                AsUtc(d.SentAt),
                "Catalog sent",
                $"Sent \"{catalogName}\" ({documentName}) via WhatsApp.",
                d.StaffUserId,
                d.StaffUser?.Name,
                "CatalogDocument",
                d.CatalogDocumentId);
        }

        // M6/E8-06: the invoice half of a dispatch. D-67 RESOLVED — this is its own
        // InvoiceDispatched kind, not a reuse of CatalogDispatched. `kind` is the client's only
        // semantic handle on an event (Title/Body are server-authored and colour is derived
        // from kind alone), so a shared kind would leave an invoice send uncountable separately
        // in E10's aggregates. It shares CatalogDispatched's dot colour deliberately — same
        // "sent via WhatsApp" family, and DESIGN_TOKENS has no precedent for a new one.
        // RefType="Invoice" remains the pointer to the target row.
        var invoiceNumber = d.Invoice?.InvoiceNumber ?? "(unknown invoice)";

        return new TimelineEventDto(
            TimelineEventKinds.InvoiceDispatched,
            AsUtc(d.SentAt),
            "Invoice sent",
            $"Sent invoice {invoiceNumber} via WhatsApp.",
            d.StaffUserId,
            d.StaffUser?.Name,
            "Invoice",
            d.InvoiceId);
    }

    /// <summary>M6 pass: read-only from `shipments` — see <see cref="GetTimelineAsync"/>'s doc comment.</summary>
    private static TimelineEventDto MapShipmentTimelineEvent(Shipment s)
    {
        var recordedBy = s.StatusHistory.OrderBy(h => h.ChangedAt).FirstOrDefault();
        var reference = s.Reference ?? "(no reference yet)";
        var body = string.IsNullOrWhiteSpace(s.Destination)
            ? $"Shipment {reference} recorded."
            : $"Shipment {reference} recorded for {s.Destination}.";

        return new TimelineEventDto(
            TimelineEventKinds.ShipmentRecorded,
            AsUtc(s.CreatedAt),
            "Shipment recorded",
            body,
            recordedBy?.ChangedByUserId,
            recordedBy?.ChangedBy?.Name,
            "Shipment",
            s.Id);
    }

    /// <summary>M6/E8-04: read-only from `invoices`. See <see cref="GetTimelineAsync"/>'s doc comment.</summary>
    private static TimelineEventDto MapInvoiceCreatedTimelineEvent(Invoice i) => new(
        TimelineEventKinds.InvoiceCreated,
        AsUtc(i.CreatedAt),
        "Invoice created",
        $"Invoice {i.InvoiceNumber} created for {MoneyFormatter.Format(i.Currency, i.Amount + i.TaxAmount)}.",
        i.CreatedByUserId,
        i.CreatedBy?.Name,
        "Invoice",
        i.Id);

    /// <summary>
    /// M6/E8-04: read-only from `invoice_status_history`, skipping the FIRST row (the Draft row
    /// InvoiceService.CreateAsync writes at creation) — see <see cref="GetTimelineAsync"/>'s doc
    /// comment for why.
    /// </summary>
    private static IEnumerable<TimelineEventDto> MapInvoiceStatusChangedTimelineEvents(Invoice i) =>
        i.StatusHistory
            .OrderBy(h => h.ChangedAt)
            .Skip(1)
            .Select(h => new TimelineEventDto(
                TimelineEventKinds.InvoiceStatusChanged,
                AsUtc(h.ChangedAt),
                "Invoice status changed",
                string.IsNullOrWhiteSpace(h.Note)
                    ? $"Invoice {i.InvoiceNumber} status changed to {h.Status.Label}."
                    : $"Invoice {i.InvoiceNumber} status changed to {h.Status.Label}. {h.Note}",
                h.ChangedByUserId,
                h.ChangedBy?.Name,
                "Invoice",
                i.Id));
}
