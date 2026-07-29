namespace SourcingOps.Application.MasterData;

/// <summary>
/// Marker shared by <see cref="CategoryDto"/> and <see cref="LookupItemDto"/> so the
/// single-item CRUD operations (create/update/retire/restore) can return either shape from
/// one method signature. ASP.NET Core's <c>ControllerBase.Ok(...)</c> serializes by the
/// value's runtime type (falls back to <c>Value.GetType()</c> when <c>ObjectResult.DeclaredType</c>
/// is null), so returning this interface still produces the correct concrete JSON shape —
/// this type intentionally carries no members of its own.
/// </summary>
public interface IMasterDataItemDto;

/// <summary>
/// Categories (TECH_SPEC §6) use <c>Name</c>, not <c>Code</c>/<c>Label</c> — matches the
/// binding aggregate-read contract's <c>categories</c> shape exactly.
/// </summary>
public sealed record CategoryDto(Guid Id, string Name, int SortOrder, bool IsActive, bool IsSystemDefault) : IMasterDataItemDto;

/// <summary>Shared shape for every other configurable-master-data collection (ServiceType/LeadStatus/ShipmentStatus/InvoiceStatus/VendorStatus).</summary>
public sealed record LookupItemDto(Guid Id, string Code, string Label, int SortOrder, bool IsActive, bool IsSystemDefault) : IMasterDataItemDto;

/// <summary>The single aggregate-read shape the frontend loads once at startup (binding contract).</summary>
public sealed record MasterDataAggregateDto(
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<LookupItemDto> ServiceTypes,
    IReadOnlyList<LookupItemDto> LeadStatuses,
    IReadOnlyList<LookupItemDto> ShipmentStatuses,
    IReadOnlyList<LookupItemDto> InvoiceStatuses,
    IReadOnlyList<LookupItemDto> VendorStatuses,
    IReadOnlyList<LookupItemDto> DocumentTypes);

/// <summary>
/// Single request shape for create/update across every collection. Categories only ever
/// read <see cref="Name"/>; every other collection reads <see cref="Code"/> (create only —
/// PUT never changes it, see <see cref="MasterDataService"/>'s doc comment) and <see cref="Label"/>.
/// </summary>
public sealed record UpsertMasterDataRequest(string? Name, string? Code, string? Label);

/// <summary>One row of a bulk reorder request body: <c>[{ "id": "...", "sortOrder": 1 }, …]</c>.</summary>
public sealed record ReorderItemDto(Guid Id, int SortOrder);

/// <summary>
/// Outcome of a delete attempt (E3-08). <see cref="ConflictDetail"/> is populated only when
/// the row is still referenced — the controller surfaces it verbatim in a 409 ProblemDetails
/// <c>detail</c> so the caller knows to retire instead.
/// </summary>
public sealed record MasterDataDeleteResult(bool Deleted, bool NotFound, string? ConflictDetail)
{
    public static MasterDataDeleteResult Success() => new(true, false, null);
    public static MasterDataDeleteResult NotFoundResult() => new(false, true, null);
    public static MasterDataDeleteResult Conflict(string detail) => new(false, false, detail);
}
