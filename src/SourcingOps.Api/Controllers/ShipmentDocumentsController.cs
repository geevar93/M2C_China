using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Api.Extensions;
using SourcingOps.Application.Shipments;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// ACTION_PLAN E7-09 / FR-INV-08. Documents are served only through this permission-checked,
/// authenticated endpoint — never a public static path (TECH_SPEC §8); an anonymous request
/// gets 401 from the authentication pipeline before reaching the action. Deliberately a
/// separate controller/resource root from <see cref="ShipmentsController"/>, mirroring
/// <see cref="VendorDocumentsController"/>'s split from <see cref="VendorsController"/>.
/// </summary>
[ApiController]
[Route("api/v1/shipment-documents")]
[Authorize]
public sealed class ShipmentDocumentsController : ControllerBase
{
    private readonly IShipmentDocumentService _service;

    public ShipmentDocumentsController(IShipmentDocumentService service)
    {
        _service = service;
    }

    [HttpGet("{id:guid}/download")]
    [Authorize(Policy = PermissionCodes.ShipmentsView)]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var download = await _service.DownloadDocumentAsync(id, ct);
        if (download is null)
        {
            return NotFound();
        }

        return File(download.Content, download.ContentType, download.OriginalFilename);
    }

    /// <summary>
    /// Deletes the stored file (via <c>IFileStorage.DeleteAsync</c>) as well as the row — see
    /// <see cref="ShipmentDocumentService.DeleteDocumentAsync"/>'s doc comment for the ordering
    /// rationale.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionCodes.ShipmentsEdit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _service.DeleteDocumentAsync(id, User.GetRequiredUserId(), ct);
        return deleted ? NoContent() : NotFound();
    }
}
