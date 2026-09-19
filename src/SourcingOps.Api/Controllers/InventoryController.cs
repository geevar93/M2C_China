using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Api.Extensions;
using SourcingOps.Application.Inventory;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// ACTION_PLAN E7-01…E7-04 (FR-INV-01, FR-INV-02, FR-INV-06, FR-INV-07). Reads require
/// <see cref="PermissionCodes.InventoryView"/>, writes <see cref="PermissionCodes.InventoryEdit"/>
/// — both already existed in the seeded catalog, so this pass added no permission. Every
/// mutation writes through <c>IAuditLogger</c> inside <see cref="InventoryService"/>.
///
/// Follows <see cref="VendorsController"/>'s shape exactly, including hosting the nested
/// inbound-stock sub-resource here rather than on a separate root — an inbound entry has no
/// life outside its item, unlike a document, which is downloadable by its own id.
/// </summary>
[ApiController]
[Route("api/v1/inventory")]
[Authorize]
public sealed class InventoryController : ControllerBase
{
    private readonly IInventoryService _service;

    public InventoryController(IInventoryService service)
    {
        _service = service;
    }

    /// <summary>E7-03/E7-04: search (name + sku), category/vendor/stock-level filters, paging, plus the D-k summary block.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCodes.InventoryView)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] Guid? vendorId = null,
        [FromQuery] string? stockLevel = null,
        CancellationToken ct = default)
    {
        var query = new InventoryListQuery(search, page, pageSize, categoryId, vendorId, stockLevel);
        var result = await _service.ListAsync(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCodes.InventoryView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _service.GetAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.InventoryEdit)]
    public async Task<IActionResult> Create([FromBody] CreateInventoryItemRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, User.GetRequiredUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCodes.InventoryEdit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateInventoryItemRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionCodes.InventoryEdit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _service.DeleteAsync(id, User.GetRequiredUserId(), ct);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// Multipart: <c>file</c> (full-size JPEG/PNG/WebP, ≤ 8 MB) and <c>thumbnail</c> (small
    /// client-generated preview, ≤ 200 KB). Replaces any existing image. Returns the item.
    /// </summary>
    [HttpPost("{id:guid}/image")]
    [Authorize(Policy = PermissionCodes.InventoryEdit)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(Guid id, [FromForm] IFormFile file, [FromForm] IFormFile thumbnail, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || thumbnail is null || thumbnail.Length == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Both an image and a thumbnail are required.");
        }
        if (thumbnail.Length > InventoryService.MaxThumbnailBytes)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Thumbnail is too large.");
        }

        byte[] thumbBytes;
        await using (var thumbStream = thumbnail.OpenReadStream())
        using (var ms = new MemoryStream())
        {
            await thumbStream.CopyToAsync(ms, ct);
            thumbBytes = ms.ToArray();
        }

        await using var imageStream = file.OpenReadStream();
        var result = await _service.SetImageAsync(id, imageStream, file.Length, thumbBytes, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}/image")]
    [Authorize(Policy = PermissionCodes.InventoryEdit)]
    public async Task<IActionResult> RemoveImage(Guid id, CancellationToken ct)
    {
        var result = await _service.RemoveImageAsync(id, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/image")]
    [Authorize(Policy = PermissionCodes.InventoryView)]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        var image = await _service.GetImageAsync(id, ct);
        return image is null ? NotFound() : File(image.Content, image.ContentType);
    }

    /// <summary>E7-02: records an inbound entry and raises on-hand quantity in one transaction.</summary>
    [HttpPost("{id:guid}/inbound")]
    [Authorize(Policy = PermissionCodes.InventoryEdit)]
    public async Task<IActionResult> RecordInbound(Guid id, [FromBody] RecordInboundRequest request, CancellationToken ct)
    {
        var result = await _service.RecordInboundAsync(id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>E7-02: the item's inbound entries, newest first.</summary>
    [HttpGet("{id:guid}/inbound")]
    [Authorize(Policy = PermissionCodes.InventoryView)]
    public async Task<IActionResult> ListInbound(Guid id, CancellationToken ct)
    {
        var result = await _service.ListInboundEntriesAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// N-38: records a physical-count correction and sets on-hand quantity to the counted
    /// value in one transaction. Gated on <see cref="PermissionCodes.InventoryAdjust"/> rather
    /// than <see cref="PermissionCodes.InventoryEdit"/> — see that constant's doc comment.
    /// </summary>
    [HttpPost("{id:guid}/adjustments")]
    [Authorize(Policy = PermissionCodes.InventoryAdjust)]
    public async Task<IActionResult> RecordAdjustment(Guid id, [FromBody] RecordAdjustmentRequest request, CancellationToken ct)
    {
        var result = await _service.RecordAdjustmentAsync(id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>N-38: the item's stock-adjustment history, newest first. Stays on Inventory.View — viewing history is not restricted.</summary>
    [HttpGet("{id:guid}/adjustments")]
    [Authorize(Policy = PermissionCodes.InventoryView)]
    public async Task<IActionResult> ListAdjustments(Guid id, CancellationToken ct)
    {
        var result = await _service.ListStockAdjustmentsAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
