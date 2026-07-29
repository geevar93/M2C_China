using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Api.Extensions;
using SourcingOps.Application.Vendors;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// Binding cross-track contract fixed by the coordinator (ACTION_PLAN E5). Reads require
/// <see cref="PermissionCodes.VendorsView"/>, writes <see cref="PermissionCodes.VendorsEdit"/>.
/// Every mutation writes through <c>IAuditLogger</c> inside <see cref="VendorService"/>.
/// </summary>
[ApiController]
[Route("api/v1/vendors")]
[Authorize]
public sealed class VendorsController : ControllerBase
{
    private readonly IVendorService _service;

    public VendorsController(IVendorService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.VendorsView)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] string? region = null,
        [FromQuery] Guid? statusId = null,
        CancellationToken ct = default)
    {
        var query = new VendorListQuery(search, page, pageSize, categoryId, region, statusId);
        var result = await _service.ListAsync(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCodes.VendorsView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _service.GetAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.VendorsEdit)]
    public async Task<IActionResult> Create([FromBody] CreateVendorRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, User.GetRequiredUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCodes.VendorsEdit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateVendorRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }
}
