namespace SourcingOps.Application.Crm;

/// <summary>
/// Binding cross-track contract fixed by the coordinator (ACTION_PLAN E4). Master data is
/// returned as IDs only (serviceTypeId/statusId/categoryIds) — the frontend's
/// MasterDataService resolves labels from one cached call, per the coordinator's explicit
/// rule against duplicating that. The one deliberate exception is <c>ownerName</c>/<c>actorName</c>:
/// users are not in the master-data cache.
/// </summary>
public sealed record CustomerListItemDto(
    Guid Id,
    string Name,
    string? BusinessName,
    string Phone,
    string? City,
    string? Region,
    string? SourceChannel,
    Guid ServiceTypeId,
    Guid StatusId,
    IReadOnlyList<Guid> CategoryIds,
    Guid? OwnerUserId,
    string? OwnerName,
    IReadOnlyList<string> Tags,
    DateTime CreatedAt);

/// <summary>CustomerListItem + the six freight-only external-purchase fields (E4-12, FSD Q1).</summary>
public sealed record CustomerDetailDto(
    Guid Id,
    string Name,
    string? BusinessName,
    string Phone,
    string? City,
    string? Region,
    string? SourceChannel,
    Guid ServiceTypeId,
    Guid StatusId,
    IReadOnlyList<Guid> CategoryIds,
    Guid? OwnerUserId,
    string? OwnerName,
    IReadOnlyList<string> Tags,
    DateTime CreatedAt,
    string? Email,
    string? Notes,
    string? ExternalMarketplace,
    string? ExternalOrderRef,
    string? ExternalSupplierName,
    decimal? ExternalOrderValue,
    string? ExternalOrderCurrency,
    DateTime? ExternalOrderDate);

public sealed record CustomerListResultDto(IReadOnlyList<CustomerListItemDto> Items, int Page, int PageSize, int TotalCount);

public sealed record InteractionDto(
    Guid Id,
    Guid CustomerId,
    string Type,
    string Text,
    DateTime? FollowUpDate,
    Guid AuthorUserId,
    string AuthorName,
    DateTime CreatedAtUtc);

public sealed record DueFollowUpDto(
    Guid InteractionId,
    Guid CustomerId,
    string CustomerName,
    string Text,
    DateTime FollowUpDate,
    string AuthorName);

/// <summary>
/// The one chronological feed (E4-07). <see cref="Kind"/> is one of <see cref="TimelineEventKinds"/>.
/// Deliberately carries no colour field — the frontend derives colour from <see cref="Kind"/>
/// (the coordinator's explicit rule; colour belongs in one place only).
/// <see cref="OccurredAtUtc"/> is always ISO 8601 UTC, never a pre-formatted display string.
/// </summary>
public sealed record TimelineEventDto(
    string Kind,
    DateTime OccurredAtUtc,
    string Title,
    string Body,
    Guid? ActorUserId,
    string? ActorName,
    string? RefType,
    Guid? RefId);

/// <summary>
/// The full kind vocabulary the frontend switches on. Only <see cref="EnquiryCaptured"/>,
/// <see cref="NoteAdded"/>, <see cref="StatusChanged"/> and <see cref="OwnerChanged"/> are
/// populated by this pass (sourced from `interactions`) — <see cref="CatalogDispatched"/>
/// (E9), <see cref="ShipmentRecorded"/> (E7) and <see cref="InvoiceCreated"/>/
/// <see cref="InvoiceStatusChanged"/> (E8) are structural placeholders in the response shape
/// only; nothing produces them yet because those modules don't exist until M4/M5/M6.
/// </summary>
public static class TimelineEventKinds
{
    public const string EnquiryCaptured = "EnquiryCaptured";
    public const string NoteAdded = "NoteAdded";
    public const string StatusChanged = "StatusChanged";
    public const string OwnerChanged = "OwnerChanged";
    public const string CatalogDispatched = "CatalogDispatched";
    public const string ShipmentRecorded = "ShipmentRecorded";
    public const string InvoiceCreated = "InvoiceCreated";
    public const string InvoiceStatusChanged = "InvoiceStatusChanged";
}

public sealed record CreateCustomerRequest(
    string Name,
    string? BusinessName,
    string Phone,
    string? Email,
    string? City,
    string? Region,
    string? SourceChannel,
    Guid ServiceTypeId,
    Guid? StatusId,
    IReadOnlyList<Guid>? CategoryIds,
    Guid? OwnerUserId,
    IReadOnlyList<string>? Tags,
    string? Notes,
    string? ExternalMarketplace,
    string? ExternalOrderRef,
    string? ExternalSupplierName,
    decimal? ExternalOrderValue,
    string? ExternalOrderCurrency,
    DateTime? ExternalOrderDate,
    bool ConfirmDuplicate = false);

/// <summary>
/// Deliberately excludes <see cref="CustomerListItemDto.OwnerUserId"/> — owner changes go
/// exclusively through <c>PUT /customers/{id}/owner</c> (E4-09) so that path is the only place
/// an "OwnerChanged" timeline event and its audit entry are produced, matching the binding
/// contract's dedicated endpoint.
/// </summary>
public sealed record UpdateCustomerRequest(
    string Name,
    string? BusinessName,
    string Phone,
    string? Email,
    string? City,
    string? Region,
    string? SourceChannel,
    Guid ServiceTypeId,
    Guid StatusId,
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<string>? Tags,
    string? Notes,
    string? ExternalMarketplace,
    string? ExternalOrderRef,
    string? ExternalSupplierName,
    decimal? ExternalOrderValue,
    string? ExternalOrderCurrency,
    DateTime? ExternalOrderDate);

/// <summary><see cref="Type"/> defaults to "Note" when omitted; the three system-reserved type strings are rejected (see <see cref="CustomerService"/>).</summary>
public sealed record CreateInteractionRequest(string? Type, string Text, DateTime? FollowUpDate);

public sealed record ChangeOwnerRequest(Guid? OwnerUserId);

/// <summary>Outcome of <c>POST /customers</c> — either a created customer or a surfaced duplicate (FR-CRM-09, E4-10).</summary>
public sealed record CreateCustomerOutcome(CustomerDetailDto? Created, CustomerListItemDto? DuplicateExisting)
{
    public static CreateCustomerOutcome Success(CustomerDetailDto detail) => new(detail, null);
    public static CreateCustomerOutcome Duplicate(CustomerListItemDto existing) => new(null, existing);
}
