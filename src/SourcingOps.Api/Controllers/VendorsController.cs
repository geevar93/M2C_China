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
///
/// Also hosts E5-07's nested document list/upload (<c>GET</c>/<c>POST {id}/documents</c>) —
/// same nesting pattern <see cref="CatalogSectionsController"/> uses for its own upload
/// route. Download and delete live on the separate <see cref="VendorDocumentsController"/>
/// resource root, mirroring <see cref="CatalogDocumentsController"/>.
/// </summary>
[ApiController]
[Route("api/v1/vendors")]
[Authorize]
public sealed class VendorsController : ControllerBase
{
    private readonly IVendorService _service;
    private readonly IVendorDocumentService _documentService;

    public VendorsController(IVendorService service, IVendorDocumentService documentService)
    {
        _service = service;
        _documentService = documentService;
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

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionCodes.VendorsEdit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _service.DeleteAsync(id, User.GetRequiredUserId(), ct);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>E5-07: non-catalog vendor documents (licence, quality certs) filed for reference only — see VendorDocumentService's doc comment.</summary>
    [HttpGet("{id:guid}/documents")]
    [Authorize(Policy = PermissionCodes.VendorsView)]
    public async Task<IActionResult> ListDocuments(Guid id, CancellationToken ct)
    {
        var result = await _documentService.ListAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>E5-07: multipart PDF upload; validation failures surface as 400 ProblemDetails via AppValidationException (ExceptionHandlingMiddleware).</summary>
    [HttpPost("{id:guid}/documents")]
    [Authorize(Policy = PermissionCodes.VendorsEdit)]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocument(Guid id, [FromForm] IFormFile file, [FromForm] Guid docTypeId, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "File is required.");
        }

        await using var stream = file.OpenReadStream();
        var result = await _documentService.UploadDocumentAsync(id, stream, file.FileName, file.ContentType, file.Length, docTypeId, User.GetRequiredUserId(), ct);

        return result is null ? NotFound() : StatusCode(StatusCodes.Status201Created, result);
    }
}
