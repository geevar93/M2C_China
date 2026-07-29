using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Api.Extensions;
using SourcingOps.Application.Vendors;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// ACTION_PLAN E5-07 / FR-VEN-07. Documents are served only through this permission-checked,
/// authenticated endpoint — never a public static path (TECH_SPEC §8). Deliberately a
/// separate controller/resource root from <see cref="VendorsController"/>, mirroring
/// <see cref="CatalogDocumentsController"/>'s split from <see cref="CatalogSectionsController"/>.
/// </summary>
[ApiController]
[Route("api/v1/vendor-documents")]
[Authorize]
public sealed class VendorDocumentsController : ControllerBase
{
    private readonly IVendorDocumentService _service;

    public VendorDocumentsController(IVendorDocumentService service)
    {
        _service = service;
    }

    [HttpGet("{id:guid}/download")]
    [Authorize(Policy = PermissionCodes.VendorsView)]
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
    /// <see cref="VendorDocumentService.DeleteDocumentAsync"/>'s doc comment for the ordering
    /// rationale.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionCodes.VendorsEdit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _service.DeleteDocumentAsync(id, User.GetRequiredUserId(), ct);
        return deleted ? NoContent() : NotFound();
    }
}
