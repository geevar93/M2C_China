using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Api.Extensions;
using SourcingOps.Application.Shipments;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// ACTION_PLAN E7-05…E7-10 (FR-INV-03…FR-INV-06, FR-INV-08, FR-INV-09). Reads require
/// <see cref="PermissionCodes.ShipmentsView"/>, writes <see cref="PermissionCodes.ShipmentsEdit"/>
/// — both already existed in the seeded catalog, so this pass added no permission. Every
/// mutation writes through <c>IAuditLogger</c> inside <see cref="ShipmentService"/>.
///
/// Also hosts E7-09's nested document list/upload, the same nesting
/// <see cref="VendorsController"/> uses; download and delete live on the separate
/// <see cref="ShipmentDocumentsController"/> resource root, mirroring
/// <see cref="VendorDocumentsController"/>.
///
/// A stock decrement that would drive an item negative surfaces as 409 ProblemDetails from
/// <c>ExceptionHandlingMiddleware</c> (deviation D-g), not from this controller — the
/// middleware is where every RFC 7807 mapping lives.
/// </summary>
[ApiController]
[Route("api/v1/shipments")]
[Authorize]
public sealed class ShipmentsController : ControllerBase
{
    private readonly IShipmentService _service;
    private readonly IShipmentDocumentService _documentService;

    public ShipmentsController(IShipmentService service, IShipmentDocumentService documentService)
    {
        _service = service;
        _documentService = documentService;
    }

    /// <summary>E7-08: status/customer/date-range filters, search on reference, paging, plus the per-status tab counts.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCodes.ShipmentsView)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] Guid? statusId = null,
        [FromQuery] Guid? customerId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var query = new ShipmentListQuery(search, page, pageSize, statusId, customerId, from, to);
        var result = await _service.ListAsync(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCodes.ShipmentsView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _service.GetAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.ShipmentsEdit)]
    public async Task<IActionResult> Create([FromBody] CreateShipmentRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, User.GetRequiredUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCodes.ShipmentsEdit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateShipmentRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionCodes.ShipmentsEdit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _service.DeleteAsync(id, User.GetRequiredUserId(), ct);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>E7-07: any configured status may be set; the no-op transition is rejected 400. Writes a history row AND an audit entry.</summary>
    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = PermissionCodes.ShipmentsEdit)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeShipmentStatusRequest request, CancellationToken ct)
    {
        var result = await _service.ChangeStatusAsync(id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>E7-09: packing list / AWB / BL reference documents attached to this shipment.</summary>
    [HttpGet("{id:guid}/documents")]
    [Authorize(Policy = PermissionCodes.ShipmentsView)]
    public async Task<IActionResult> ListDocuments(Guid id, CancellationToken ct)
    {
        var result = await _documentService.ListAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>E7-09: multipart PDF upload; validation failures surface as 400 ProblemDetails via AppValidationException (ExceptionHandlingMiddleware).</summary>
    [HttpPost("{id:guid}/documents")]
    [Authorize(Policy = PermissionCodes.ShipmentsEdit)]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocument(Guid id, [FromForm] IFormFile file, [FromForm] Guid documentTypeId, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "File is required.");
        }

        await using var stream = file.OpenReadStream();
        var result = await _documentService.UploadDocumentAsync(
            id, stream, file.FileName, file.ContentType, file.Length, documentTypeId, User.GetRequiredUserId(), ct);

        return result is null ? NotFound() : StatusCode(StatusCodes.Status201Created, result);
    }
}
