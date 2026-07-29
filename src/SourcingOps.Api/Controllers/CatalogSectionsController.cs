using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Api.Extensions;
using SourcingOps.Application.Catalog;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// Binding cross-track contract fixed by the coordinator (ACTION_PLAN E6). Reads require
/// <see cref="PermissionCodes.CatalogsView"/>, writes (including document upload)
/// <see cref="PermissionCodes.CatalogsEdit"/>. Every mutation writes through
/// <c>IAuditLogger</c> inside <see cref="CatalogService"/>. Document download lives on
/// <see cref="CatalogDocumentsController"/> — a different resource root
/// (<c>/api/v1/catalog-documents/{id}/download</c>), per the binding contract.
/// </summary>
[ApiController]
[Route("api/v1/catalog-sections")]
[Authorize]
public sealed class CatalogSectionsController : ControllerBase
{
    private readonly ICatalogService _service;

    public CatalogSectionsController(ICatalogService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCodes.CatalogsView)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] Guid? vendorId = null,
        [FromQuery] string? tag = null,
        CancellationToken ct = default)
    {
        var query = new CatalogSectionListQuery(search, page, pageSize, categoryId, vendorId, tag);
        var result = await _service.ListAsync(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCodes.CatalogsView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _service.GetAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.CatalogsEdit)]
    public async Task<IActionResult> Create([FromBody] CreateCatalogSectionRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, User.GetRequiredUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCodes.CatalogsEdit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCatalogSectionRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionCodes.CatalogsEdit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _service.DeleteAsync(id, User.GetRequiredUserId(), ct);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>E6-02/E6-03/E6-06: multipart PDF upload; validation failures surface as 400 ProblemDetails via AppValidationException (ExceptionHandlingMiddleware).</summary>
    [HttpPost("{id:guid}/documents")]
    [Authorize(Policy = PermissionCodes.CatalogsEdit)]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocument(Guid id, [FromForm] IFormFile file, [FromForm] string? versionLabel, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "File is required.");
        }

        await using var stream = file.OpenReadStream();
        var result = await _service.UploadDocumentAsync(id, stream, file.FileName, file.ContentType, file.Length, versionLabel, User.GetRequiredUserId(), ct);

        return result is null ? NotFound() : StatusCode(StatusCodes.Status201Created, result);
    }
}
