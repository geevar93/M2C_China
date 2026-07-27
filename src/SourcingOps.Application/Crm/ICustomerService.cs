namespace SourcingOps.Application.Crm;

/// <summary>Implements ACTION_PLAN E4-01…E4-12 behind the coordinator's binding <c>/api/v1/customers</c> contract.</summary>
public interface ICustomerService
{
    Task<CustomerListResultDto> ListAsync(CustomerListQuery query, CancellationToken ct = default);

    Task<CustomerDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns a <see cref="CreateCustomerOutcome.DuplicateExisting"/> instead of creating, per FR-CRM-09/E4-10, unless <see cref="CreateCustomerRequest.ConfirmDuplicate"/> is true.</summary>
    Task<CreateCustomerOutcome> CreateAsync(CreateCustomerRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Null return means the customer id was not found.</summary>
    Task<CustomerDetailDto?> UpdateAsync(Guid id, UpdateCustomerRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Null return means the customer id was not found. Newest first (E4-07).</summary>
    Task<IReadOnlyList<TimelineEventDto>?> GetTimelineAsync(Guid id, CancellationToken ct = default);

    /// <summary>Null return means the customer id was not found.</summary>
    Task<InteractionDto?> AddInteractionAsync(Guid id, CreateInteractionRequest request, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Null return means the customer id was not found. Idempotent — reassigning to the current owner is a no-op that still returns the current detail.</summary>
    Task<CustomerDetailDto?> ChangeOwnerAsync(Guid id, Guid? ownerUserId, Guid actorUserId, CancellationToken ct = default);

    /// <summary>E4-08: interactions whose <c>follow_up_date</c> is at or before <paramref name="asOfUtc"/>, earliest due first.</summary>
    Task<IReadOnlyList<DueFollowUpDto>> GetDueFollowUpsAsync(DateTime asOfUtc, CancellationToken ct = default);
}

/// <summary>All six filters plus search/paging from the binding <c>GET /customers</c> contract.</summary>
public sealed record CustomerListQuery(
    string? Search,
    int Page,
    int PageSize,
    Guid? StatusId,
    Guid? ServiceTypeId,
    Guid? CategoryId,
    string? Region,
    Guid? OwnerUserId,
    string? Tag);
