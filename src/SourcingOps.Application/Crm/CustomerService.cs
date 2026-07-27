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

        var statusChanged = customer.StatusId != status.Id;
        var previousStatusLabel = statusChanged ? customer.Status?.Label ?? (await _db.LeadStatuses.FindAsync([customer.StatusId], ct))?.Label : null;

        customer.Name = name;
        customer.BusinessName = Trim(request.BusinessName);
        customer.Phone = normalizedPhone;
        customer.Email = Trim(request.Email);
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

        var interactions = await _db.Interactions
            .Include(i => i.Author)
            .Where(i => i.CustomerId == id)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        // Live sources today: interactions (Note/Call/etc → NoteAdded, plus the three
        // system-generated types below). CatalogDispatched/ShipmentRecorded/InvoiceCreated/
        // InvoiceStatusChanged are structural only — no data exists for them until M4/M5/M6
        // build dispatches/shipments/invoices; the response shape already accommodates them
        // (RefType/RefId) without change.
        return interactions.Select(MapTimelineEvent).ToList();
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
        c.ExternalOrderValue, c.ExternalOrderCurrency, AsUtcOrNull(c.ExternalOrderDate));

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
        // (Shipment | CatalogDocument | Invoice | null); those are reserved for the
        // structural placeholder kinds this pass does not populate.
        return new TimelineEventDto(kind, AsUtc(i.CreatedAt), title, i.Text, i.AuthorUserId, i.Author?.Name, null, null);
    }
}
