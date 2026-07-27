using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Api.Extensions;
using SourcingOps.Application.Crm;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// Binding cross-track contract fixed by the coordinator (ACTION_PLAN E4). Reads require
/// <see cref="PermissionCodes.CustomersView"/>, writes <see cref="PermissionCodes.CustomersEdit"/>.
/// Every mutation writes through <c>IAuditLogger</c> inside <see cref="CustomerService"/> (DR-8).
/// </summary>
[ApiController]
[Route("api/v1/customers")]
[Authorize]
public sealed class CustomersController : ControllerBase
{
    private readonly ICustomerService _service;

    public CustomersController(ICustomerService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.CustomersView)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] Guid? statusId = null,
        [FromQuery] Guid? serviceTypeId = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] string? region = null,
        [FromQuery] Guid? ownerUserId = null,
        [FromQuery] string? tag = null,
        CancellationToken ct = default)
    {
        var query = new CustomerListQuery(search, page, pageSize, statusId, serviceTypeId, categoryId, region, ownerUserId, tag);
        var result = await _service.ListAsync(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCodes.CustomersView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _service.GetAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.CustomersEdit)]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        var outcome = await _service.CreateAsync(request, User.GetRequiredUserId(), ct);

        if (outcome.DuplicateExisting is not null)
        {
            // FR-CRM-09 / E4-10: surface the existing record and require an explicit
            // confirmDuplicate:true to proceed — one mechanism, no extra round trip.
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A customer with this phone number already exists.",
                detail: "Pass confirmDuplicate: true in the request body to create a new record anyway.",
                extensions: new Dictionary<string, object?> { ["existingCustomer"] = outcome.DuplicateExisting });
        }

        return StatusCode(StatusCodes.Status201Created, outcome.Created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCodes.CustomersEdit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/timeline")]
    [Authorize(Policy = PermissionCodes.CustomersView)]
    public async Task<IActionResult> Timeline(Guid id, CancellationToken ct)
    {
        var result = await _service.GetTimelineAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/interactions")]
    [Authorize(Policy = PermissionCodes.CustomersEdit)]
    public async Task<IActionResult> AddInteraction(Guid id, [FromBody] CreateInteractionRequest request, CancellationToken ct)
    {
        var result = await _service.AddInteractionAsync(id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}/owner")]
    [Authorize(Policy = PermissionCodes.CustomersEdit)]
    public async Task<IActionResult> ChangeOwner(Guid id, [FromBody] ChangeOwnerRequest request, CancellationToken ct)
    {
        var result = await _service.ChangeOwnerAsync(id, request.OwnerUserId, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("follow-ups/due")]
    [Authorize(Policy = PermissionCodes.CustomersView)]
    public async Task<IActionResult> DueFollowUps([FromQuery] DateTime? asOf, CancellationToken ct)
    {
        var asOfUtc = asOf.HasValue ? DateTime.SpecifyKind(asOf.Value, DateTimeKind.Utc) : DateTime.UtcNow;
        var result = await _service.GetDueFollowUpsAsync(asOfUtc, ct);
        return Ok(result);
    }
}
