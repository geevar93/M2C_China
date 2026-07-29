using Microsoft.EntityFrameworkCore;
using SourcingOps.Application.Common;
using SourcingOps.Application.Interfaces;
using SourcingOps.Domain.Constants;
using SourcingOps.Domain.Entities;

namespace SourcingOps.Application.Shipments;

/// <summary>
/// Implements ACTION_PLAN E7-05…E7-08 and E7-10. Follows <c>VendorService</c>'s structure:
/// list queries materialize the current page and map client-side rather than projecting inside
/// the expression tree, so the query shape is correct against both Npgsql and the EF Core
/// InMemory provider the Application-layer unit tests use.
///
/// Every mutating path funnels its stock movement through <see cref="ApplyStockDeltasAsync"/>
/// and commits with ONE <c>SaveChangesAsync</c>. EF Core wraps a single SaveChanges in a
/// transaction, so E7-06's "in the same transaction" holds without an explicit
/// <c>BeginTransaction</c> — which is not available here anyway, since <c>IAppDbContext</c>
/// deliberately exposes only <c>SaveChangesAsync</c> (TECH_SPEC §4.1's narrow seam).
/// </summary>
public sealed class ShipmentService : IShipmentService
{
    /// <summary>
    /// D-i: attempts at generating a unique <c>SHP-YYMM-NNN</c> reference before giving up.
    /// Each retry re-reads the current maximum, so a lost race is resolved by taking the next
    /// number rather than by backing off. Five is far beyond what two concurrent creates need;
    /// the limit exists only so a genuine, non-reference unique violation cannot spin forever.
    /// </summary>
    private const int MaxReferenceAttempts = 5;

    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;

    public ShipmentService(IAppDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    // ---- List / filter (E7-08) --------------------------------------------------------

    public async Task<ShipmentListResultDto> ListAsync(ShipmentListQuery query, CancellationToken ct = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 200 ? 25 : query.PageSize;

        if (query.From.HasValue && query.To.HasValue && query.From.Value > query.To.Value)
        {
            throw new AppValidationException("from", "'from' must not be later than 'to'.");
        }

        // Everything EXCEPT the status filter. The status tabs count across the whole set, so
        // they must not be narrowed by the tab currently selected — otherwise every tab would
        // read either its own total or zero.
        var withoutStatus = _db.Shipments.AsQueryable();

        if (query.CustomerId.HasValue)
        {
            withoutStatus = withoutStatus.Where(s => s.CustomerId == query.CustomerId.Value);
        }
        if (query.From.HasValue)
        {
            var from = AsUtc(query.From.Value);
            withoutStatus = withoutStatus.Where(s => s.DispatchDate != null && s.DispatchDate >= from);
        }
        if (query.To.HasValue)
        {
            var to = AsUtc(query.To.Value);
            withoutStatus = withoutStatus.Where(s => s.DispatchDate != null && s.DispatchDate <= to);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Same .ToLower().Contains(...) shape as VendorService — must translate identically
            // against Npgsql and InMemory. E7-08 scopes search to the reference, which is the
            // identifier staff actually quote.
            var term = query.Search.Trim().ToLowerInvariant();
            withoutStatus = withoutStatus.Where(s => s.Reference != null && s.Reference.ToLower().Contains(term));
        }

        var statusCounts = await BuildStatusCountsAsync(withoutStatus, ct);

        var filtered = query.StatusId.HasValue
            ? withoutStatus.Where(s => s.StatusId == query.StatusId.Value)
            : withoutStatus;

        var totalCount = await filtered.CountAsync(ct);

        var pageEntities = await filtered
            .Include(s => s.Customer)
            .Include(s => s.ServiceType)
            .Include(s => s.Status)
            .Include(s => s.Lines)
            .OrderByDescending(s => s.DispatchDate)
            .ThenByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = pageEntities.Select(MapListItem).ToList();
        return new ShipmentListResultDto(items, page, pageSize, totalCount, statusCounts);
    }

    private async Task<List<ShipmentStatusCountDto>> BuildStatusCountsAsync(IQueryable<Shipment> source, CancellationToken ct)
    {
        var counts = await source
            .GroupBy(s => s.StatusId)
            .Select(g => new { StatusId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countsById = counts.ToDictionary(c => c.StatusId, c => c.Count);

        // Every status in the lookup is returned, including the ones with zero matches — the
        // prototype's tab strip renders a tab per configured status regardless, and a missing
        // key would make the screen show no tab rather than a "0" tab. Retired statuses are
        // included too so a tab does not vanish out from under historical rows (E3-08).
        var statuses = await _db.ShipmentStatuses.ToListAsync(ct);

        return statuses
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Code, StringComparer.Ordinal)
            .Select(s => new ShipmentStatusCountDto(s.Id, s.Code, s.Label, s.SortOrder, countsById.GetValueOrDefault(s.Id, 0)))
            .ToList();
    }

    // ---- Get one (E7-05) ---------------------------------------------------------------

    public async Task<ShipmentDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var shipment = await LoadWithNavigationsAsync(id, ct);
        return shipment is null ? null : MapDetail(shipment);
    }

    // ---- Create (E7-05, E7-06, E7-10) ---------------------------------------------------

    public async Task<ShipmentDetailDto> CreateAsync(CreateShipmentRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var customer = await ResolveCustomerAsync(request.CustomerId, ct);
        var serviceType = await ResolveServiceTypeAsync(request.ServiceTypeId, ct);
        var status = await ResolveStatusAsync(request.StatusId, ct);

        var requestedLines = NormalizeLines(request.Lines);
        EnsureFreightOnlyHasNoLines(serviceType, requestedLines);

        var items = await ResolveLineItemsAsync(requestedLines, ct);

        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Customer = customer,
            Destination = Trim(request.Destination),
            ServiceTypeId = serviceType.Id,
            ServiceType = serviceType,
            DispatchDate = AsUtcOrNull(request.DispatchDate),
            StatusId = status.Id,
            Status = status,
            FreightCost = request.FreightCost,
            Mode = Trim(request.Mode),
            AwbOrBl = Trim(request.AwbOrBl),
            Eta = AsUtcOrNull(request.Eta),
            CreatedAt = DateTime.UtcNow
        };

        foreach (var requested in requestedLines)
        {
            var item = items[requested.InventoryItemId];
            shipment.Lines.Add(new ShipmentLine
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                InventoryItemId = item.Id,
                InventoryItem = item,
                Quantity = requested.Quantity,
                // D-b: snapshot. Caller's value wins when supplied; otherwise the item's
                // current cost is frozen onto the line so a later price change cannot rewrite
                // the basis of an invoice E8 will raise against this shipment.
                UnitCost = requested.UnitCost ?? item.UnitCost
            });
        }

        shipment.TotalValue = ResolveTotalValue(shipment.Lines, request.TotalValue);

        // D-e: the opening status gets a history row at creation, so the detail screen's
        // stepper always has a first entry rather than an empty timeline until the first
        // transition.
        shipment.StatusHistory.Add(new ShipmentStatusHistory
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipment.Id,
            StatusId = status.Id,
            Status = status,
            ChangedByUserId = actorUserId,
            ChangedAt = DateTime.UtcNow,
            Note = "Shipment created."
        });

        // E7-06: consume stock for every line. Freight-only reaches here with an empty set and
        // therefore moves nothing (E7-10 / FSD A8) — the guard above already rejected lines.
        var deltas = requestedLines
            .GroupBy(l => l.InventoryItemId)
            .ToDictionary(g => g.Key, g => -g.Sum(l => l.Quantity));
        var overridden = ApplyStockDeltas(items, deltas, request.AllowNegativeStock);

        _db.Shipments.Add(shipment);
        await SaveWithGeneratedReferenceAsync(shipment, ct);

        await _audit.LogAsync(actorUserId, "ShipmentCreated", "Shipment", shipment.Id.ToString(), new
        {
            shipment.Reference,
            CustomerName = ResolveCustomerName(customer),
            ServiceTypeCode = serviceType.Code,
            StatusCode = status.Code,
            LineCount = shipment.Lines.Count,
            shipment.TotalValue,
            NegativeStockOverride = overridden
        }, ct);

        var reloaded = await LoadWithNavigationsAsync(shipment.Id, ct);
        return MapDetail(reloaded ?? shipment);
    }

    // ---- Update (E7-05, D-j) -------------------------------------------------------------

    public async Task<ShipmentDetailDto?> UpdateAsync(Guid id, UpdateShipmentRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var shipment = await LoadWithNavigationsAsync(id, ct);
        if (shipment is null)
        {
            return null;
        }

        var customer = await ResolveCustomerAsync(request.CustomerId, ct);
        var serviceType = await ResolveServiceTypeAsync(request.ServiceTypeId, ct);

        var requestedLines = NormalizeLines(request.Lines);
        EnsureFreightOnlyHasNoLines(serviceType, requestedLines);

        // Both the incoming items AND every item the existing lines point at must be loaded:
        // a removed line restores stock to an item the new request never mentions.
        var existingQtyByItem = shipment.Lines
            .GroupBy(l => l.InventoryItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var requestedQtyByItem = requestedLines
            .GroupBy(l => l.InventoryItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var affectedItemIds = existingQtyByItem.Keys.Union(requestedQtyByItem.Keys).ToList();
        var items = await ResolveItemsByIdAsync(affectedItemIds, ct);

        // D-j: apply the DIFFERENCE, never a fresh full decrement. Removing a line restores its
        // quantity (positive delta); raising a quantity consumes only the increase.
        var deltas = affectedItemIds.ToDictionary(
            itemId => itemId,
            itemId => existingQtyByItem.GetValueOrDefault(itemId, 0m) - requestedQtyByItem.GetValueOrDefault(itemId, 0m));

        var overridden = ApplyStockDeltas(items, deltas, request.AllowNegativeStock);

        shipment.CustomerId = customer.Id;
        shipment.Customer = customer;
        shipment.Destination = Trim(request.Destination);
        shipment.ServiceTypeId = serviceType.Id;
        shipment.ServiceType = serviceType;
        shipment.DispatchDate = AsUtcOrNull(request.DispatchDate);
        shipment.FreightCost = request.FreightCost;
        shipment.Mode = Trim(request.Mode);
        shipment.AwbOrBl = Trim(request.AwbOrBl);
        shipment.Eta = AsUtcOrNull(request.Eta);
        // StatusId deliberately untouched — see UpdateShipmentRequest's doc comment.

        // Removed through the DbSet ONLY. Deliberately does NOT also call shipment.Lines.Clear()
        // the way VendorService.UpdateAsync clears VendorCategories — the two are not the same
        // case. Severing a ShipmentLine from its parent navigation after Remove() flips the
        // entry from Deleted back to *Modified* (EF treats the cleared required FK as a property
        // edit on a now-orphaned row), and SaveChanges then fails with
        // DbUpdateConcurrencyException. VendorCategory survives that pattern only because its FK
        // is part of its composite primary key, so the sever is not representable and the entry
        // stays Deleted. Verified by inspecting ChangeTracker entries on the failure.
        foreach (var line in shipment.Lines.ToList())
        {
            _db.ShipmentLines.Remove(line);
        }

        var newLines = requestedLines.Select(requested =>
        {
            var item = items[requested.InventoryItemId];
            return new ShipmentLine
            {
                Id = Guid.NewGuid(),
                ShipmentId = shipment.Id,
                InventoryItemId = item.Id,
                InventoryItem = item,
                Quantity = requested.Quantity,
                UnitCost = requested.UnitCost ?? item.UnitCost
            };
        }).ToList();
        _db.ShipmentLines.AddRange(newLines);

        // Computed from `newLines`, not `shipment.Lines`: the navigation still holds the
        // Deleted rows until SaveChanges, so reading it here would double-count.
        shipment.TotalValue = ResolveTotalValue(newLines, request.TotalValue);

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "ShipmentUpdated", "Shipment", shipment.Id.ToString(), new
        {
            shipment.Reference,
            CustomerName = ResolveCustomerName(customer),
            ServiceTypeCode = serviceType.Code,
            LineCount = shipment.Lines.Count,
            shipment.TotalValue,
            NegativeStockOverride = overridden
        }, ct);

        var reloaded = await LoadWithNavigationsAsync(shipment.Id, ct);
        return MapDetail(reloaded ?? shipment);
    }

    // ---- Delete (D-j) ---------------------------------------------------------------------

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var shipment = await _db.Shipments
            .Include(s => s.Lines)
            .Include(s => s.Documents)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shipment is null)
        {
            return false;
        }

        // A shipment with stored documents is not deletable here: the files would be orphaned
        // on disk with no row left pointing at them. Delete the documents first, which goes
        // through ShipmentDocumentService and removes the file as well as the row.
        if (shipment.Documents.Count > 0)
        {
            throw new AppValidationException("id",
                "This shipment still has reference documents attached. Delete them first.");
        }

        // D-j: deleting a shipment restores the stock its lines consumed. Never guarded by
        // allowNegativeStock — a restore only ever raises on-hand quantity.
        var restoreDeltas = shipment.Lines
            .GroupBy(l => l.InventoryItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var items = await ResolveItemsByIdAsync(restoreDeltas.Keys.ToList(), ct);
        ApplyStockDeltas(items, restoreDeltas, allowNegativeStock: true);

        _db.Shipments.Remove(shipment);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "ShipmentDeleted", "Shipment", id.ToString(),
            new { shipment.Reference, RestoredLineCount = restoreDeltas.Count }, ct);
        return true;
    }

    // ---- Status transition (E7-07, D-e) ---------------------------------------------------

    public async Task<ShipmentDetailDto?> ChangeStatusAsync(Guid id, ChangeShipmentStatusRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var shipment = await LoadWithNavigationsAsync(id, ct);
        if (shipment is null)
        {
            return null;
        }

        var status = await ResolveStatusAsync(request.StatusId, ct);

        // ANY status may be set. The lookup is user-configurable (E3-06/FSD §3.3), so a
        // hard-coded legal-transition graph would break the moment the business adds a stage —
        // the only rejected move is the no-op.
        if (shipment.StatusId == status.Id)
        {
            throw new AppValidationException("statusId", $"This shipment is already at status '{status.Label}'.");
        }

        var previousStatusCode = shipment.Status?.Code;

        shipment.StatusId = status.Id;
        shipment.Status = status;

        // Note there is no "Cancelled" shipment status in the seeded set, so a transition never
        // restores stock — only line edits and deletion do (D-j).
        var history = new ShipmentStatusHistory
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipment.Id,
            StatusId = status.Id,
            Status = status,
            ChangedByUserId = actorUserId,
            ChangedAt = DateTime.UtcNow,
            Note = Trim(request.Note)
        };
        // Added through the DbSet ONLY. Also adding it to shipment.StatusHistory would land it
        // in that collection TWICE — EF Core's relationship fixup populates the inverse
        // navigation itself once the principal is tracked, and the collection is a plain List
        // with no de-duplication. The reload below does not clear an already-populated tracked
        // collection either, so the duplicate would survive all the way into the response.
        _db.ShipmentStatusHistory.Add(history);

        await _db.SaveChangesAsync(ct);

        // BOTH the history row above and this audit entry are written. E7-07 requires
        // transitions be "audit-logged and timestamped"; they serve different readers — the
        // audit log is the cross-entity compliance trail, the history table is the shipment's
        // own displayable stepper (D-e).
        await _audit.LogAsync(actorUserId, "ShipmentStatusChanged", "Shipment", shipment.Id.ToString(),
            new { shipment.Reference, FromStatusCode = previousStatusCode, ToStatusCode = status.Code, history.Note }, ct);

        var reloaded = await LoadWithNavigationsAsync(shipment.Id, ct);
        return MapDetail(reloaded ?? shipment);
    }

    // ---- Stock movement (E7-06, D-g, D-j) --------------------------------------------------

    /// <summary>
    /// Applies <paramref name="deltas"/> (negative consumes, positive restores) to the loaded
    /// items in memory; the caller commits. Returns true when at least one delta was allowed to
    /// drive stock negative under an explicit override, so the caller can record that fact in
    /// the audit detail.
    ///
    /// Every offending item is collected before throwing, so a caller fixing a multi-line
    /// shipment sees all the shortfalls at once rather than discovering them one save at a time.
    /// </summary>
    private static bool ApplyStockDeltas(
        IReadOnlyDictionary<Guid, InventoryItem> items,
        IReadOnlyDictionary<Guid, decimal> deltas,
        bool allowNegativeStock)
    {
        var shortfalls = new List<InsufficientStockDetail>();
        var overrodeNegative = false;

        foreach (var (itemId, delta) in deltas)
        {
            if (delta == 0m)
            {
                continue;
            }

            var item = items[itemId];
            var resulting = item.OnHandQty + delta;

            if (resulting < 0m && delta < 0m)
            {
                if (!allowNegativeStock)
                {
                    shortfalls.Add(new InsufficientStockDetail(item.Id, item.Name, item.Sku, -delta, item.OnHandQty));
                    continue;
                }

                overrodeNegative = true;
            }

            item.OnHandQty = resulting;
        }

        if (shortfalls.Count > 0)
        {
            throw new InsufficientStockException(shortfalls);
        }

        return overrodeNegative;
    }

    // ---- Reference generation (D-i) ---------------------------------------------------------

    /// <summary>
    /// Generates <c>SHP-YYMM-NNN</c> inside the creating transaction and retries on a unique
    /// violation, so two concurrent creates cannot collide on the partial unique index. Each
    /// attempt re-reads the current maximum for the month rather than blindly incrementing, so
    /// the loser of a race takes the next free number.
    /// </summary>
    private async Task SaveWithGeneratedReferenceAsync(Shipment shipment, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            shipment.Reference = await GenerateReferenceAsync(ct);

            try
            {
                await _db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (UniqueViolationDetector.IsUniqueViolation(ex) && attempt < MaxReferenceAttempts)
            {
                // The entities stay in the Added state after a failed SaveChanges, so the next
                // iteration only needs to overwrite Reference and try again.
            }
        }
    }

    private async Task<string> GenerateReferenceAsync(CancellationToken ct)
    {
        var prefix = $"SHP-{DateTime.UtcNow:yyMM}-";

        var existing = await _db.Shipments
            .Where(s => s.Reference != null && s.Reference.StartsWith(prefix))
            .Select(s => s.Reference!)
            .ToListAsync(ct);

        var maxSequence = existing
            .Select(reference => int.TryParse(reference[prefix.Length..], out var parsed) ? parsed : 0)
            .DefaultIfEmpty(0)
            .Max();

        // D3 pads to three digits and simply widens past 999 rather than wrapping — a 1000th
        // shipment in one month keeps a unique, sortable reference instead of colliding.
        return $"{prefix}{maxSequence + 1:D3}";
    }

    // ---- Validation helpers ------------------------------------------------------------------

    /// <summary>
    /// E7-10 / FSD A8 / deviation D-h. Switches on the service type's <c>Code</c>, never its
    /// label or id: <c>Code</c> is immutable by design (D-12) while labels are user-editable
    /// and ids differ per environment.
    /// </summary>
    private static void EnsureFreightOnlyHasNoLines(ServiceType serviceType, IReadOnlyList<ShipmentLineRequest> lines)
    {
        if (lines.Count > 0 && string.Equals(serviceType.Code, SeedDefaults.ServiceTypeFreightOnly, StringComparison.Ordinal))
        {
            throw new AppValidationException("lines",
                "A freight-only shipment carries no inventory lines and moves no stock. Remove the lines or change the service type.");
        }
    }

    private static List<ShipmentLineRequest> NormalizeLines(IReadOnlyList<ShipmentLineRequest>? lines)
    {
        if (lines is null || lines.Count == 0)
        {
            return [];
        }

        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Quantity <= 0)
            {
                errors[$"lines[{i}].quantity"] = ["Line quantity must be greater than zero."];
            }
            if (lines[i].UnitCost is < 0)
            {
                errors[$"lines[{i}].unitCost"] = ["Line unit cost cannot be negative."];
            }
        }

        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }

        return lines.ToList();
    }

    /// <summary>
    /// D-c: computed from the lines whenever there are any, so a client-supplied total can
    /// never contradict them. Accepted from the caller only for a shipment with no lines, which
    /// is the freight-only case (its value is the freight charge, not goods value).
    /// </summary>
    private static decimal? ResolveTotalValue(ICollection<ShipmentLine> lines, decimal? requested)
    {
        if (lines.Count == 0)
        {
            return requested;
        }

        return lines.Sum(l => l.Quantity * (l.UnitCost ?? 0m));
    }

    private async Task<Dictionary<Guid, InventoryItem>> ResolveLineItemsAsync(IReadOnlyList<ShipmentLineRequest> lines, CancellationToken ct) =>
        await ResolveItemsByIdAsync(lines.Select(l => l.InventoryItemId).Distinct().ToList(), ct);

    private async Task<Dictionary<Guid, InventoryItem>> ResolveItemsByIdAsync(IReadOnlyList<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        var distinct = itemIds.Distinct().ToList();
        var found = await _db.InventoryItems.Where(i => distinct.Contains(i.Id)).ToListAsync(ct);

        if (found.Count != distinct.Count)
        {
            var foundSet = found.Select(i => i.Id).ToHashSet();
            var unknown = distinct.Where(itemId => !foundSet.Contains(itemId));
            throw new AppValidationException("lines", $"Unknown inventory item id(s): {string.Join(", ", unknown)}.");
        }

        return found.ToDictionary(i => i.Id);
    }

    private async Task<Customer> ResolveCustomerAsync(Guid customerId, CancellationToken ct) =>
        await _db.Customers.FindAsync([customerId], ct)
            ?? throw new AppValidationException("customerId", "Unknown customer.");

    private async Task<ServiceType> ResolveServiceTypeAsync(Guid serviceTypeId, CancellationToken ct) =>
        await _db.ServiceTypes.FindAsync([serviceTypeId], ct)
            ?? throw new AppValidationException("serviceTypeId", "Unknown service type.");

    private async Task<Domain.Entities.ShipmentStatus> ResolveStatusAsync(Guid statusId, CancellationToken ct) =>
        await _db.ShipmentStatuses.FindAsync([statusId], ct)
            ?? throw new AppValidationException("statusId", "Unknown shipment status.");

    // ---- Loading / mapping --------------------------------------------------------------------

    private Task<Shipment?> LoadWithNavigationsAsync(Guid id, CancellationToken ct) =>
        _db.Shipments
            .Include(s => s.Customer)
            .Include(s => s.ServiceType)
            .Include(s => s.Status)
            .Include(s => s.Lines).ThenInclude(l => l.InventoryItem)
            .Include(s => s.StatusHistory).ThenInclude(h => h.Status)
            .Include(s => s.StatusHistory).ThenInclude(h => h.ChangedBy)
            .Include(s => s.Documents).ThenInclude(d => d.DocumentType)
            .Include(s => s.Documents).ThenInclude(d => d.UploadedBy)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtcOrNull(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;

    /// <summary>Business name where present, contact name otherwise, so a list row never renders blank.</summary>
    private static string ResolveCustomerName(Customer c) =>
        string.IsNullOrWhiteSpace(c.BusinessName) ? c.Name : c.BusinessName;

    private static StatusRefDto MapStatus(Domain.Entities.ShipmentStatus s) => new(s.Id, s.Code, s.Label);

    private static StatusRefDto MapServiceType(ServiceType s) => new(s.Id, s.Code, s.Label);

    private static ShipmentListItemDto MapListItem(Shipment s) => new(
        s.Id, s.Reference,
        new CustomerRefDto(s.CustomerId, s.Customer is null ? "(unknown)" : ResolveCustomerName(s.Customer)),
        s.Destination,
        MapServiceType(s.ServiceType),
        AsUtcOrNull(s.DispatchDate),
        MapStatus(s.Status),
        s.FreightCost, s.TotalValue, s.Mode, s.AwbOrBl, AsUtcOrNull(s.Eta),
        s.Lines.Count);

    internal static ShipmentDetailDto MapDetail(Shipment s)
    {
        var history = s.StatusHistory.OrderBy(h => h.ChangedAt).ToList();

        return new ShipmentDetailDto(
            s.Id, s.Reference,
            new CustomerRefDto(s.CustomerId, s.Customer is null ? "(unknown)" : ResolveCustomerName(s.Customer)),
            s.Destination,
            MapServiceType(s.ServiceType),
            AsUtcOrNull(s.DispatchDate),
            MapStatus(s.Status),
            s.FreightCost, s.TotalValue, s.Mode, s.AwbOrBl, AsUtcOrNull(s.Eta),
            s.Lines.Count,
            AsUtc(s.CreatedAt),
            // The earliest history row's user is whoever created the shipment — see
            // ShipmentDetailDto.RecordedByName's doc comment for why this is read rather than
            // stored on a created_by_user_id column.
            history.FirstOrDefault()?.ChangedBy?.Name,
            s.Lines.OrderBy(l => l.InventoryItem?.Name ?? string.Empty).Select(MapLine).ToList(),
            history.Select(MapHistory).ToList(),
            s.Documents.OrderByDescending(d => d.UploadedAt).Select(MapDocument).ToList());
    }

    private static ShipmentLineDto MapLine(ShipmentLine l) => new(
        l.Id, l.InventoryItemId,
        l.InventoryItem?.Name ?? "(unknown)",
        l.InventoryItem?.Sku,
        l.InventoryItem?.Unit ?? "pcs",
        l.Quantity,
        l.UnitCost,
        l.UnitCost.HasValue ? l.Quantity * l.UnitCost.Value : null);

    private static ShipmentStatusHistoryDto MapHistory(ShipmentStatusHistory h) => new(
        h.Id, MapStatus(h.Status), h.ChangedByUserId, h.ChangedBy?.Name ?? "(unknown)", AsUtc(h.ChangedAt), h.Note);

    internal static ShipmentDocumentDto MapDocument(ShipmentDocument d) => new(
        d.Id, d.ShipmentId, d.OriginalFilename, d.SizeBytes,
        new StatusRefDto(d.DocumentTypeId, d.DocumentType?.Code ?? "(unknown)", d.DocumentType?.Label ?? "(unknown)"),
        d.UploadedByUserId, d.UploadedBy?.Name ?? "(unknown)", AsUtc(d.UploadedAt));
}
