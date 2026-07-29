using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Application.Catalog;
using SourcingOps.Application.Dispatching;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// E6-04: documents are served only through this permission-checked, authenticated endpoint —
/// never a public static path (TECH_SPEC §8). Deliberately a separate controller/resource root
/// from <see cref="CatalogSectionsController"/> — the binding contract's download route is
/// <c>/api/v1/catalog-documents/{id}/download</c>, not nested under catalog-sections.
///
/// Also hosts E9-07's "sent to" history sub-resource (<c>GET {id}/dispatches</c>): the
/// coordinator's brief asked for an additive endpoint rather than folding dispatch history into
/// <see cref="CatalogDocumentDto"/>'s shape — see <see cref="SentTo"/>'s doc comment for why.
/// </summary>
[ApiController]
[Route("api/v1/catalog-documents")]
[Authorize]
public sealed class CatalogDocumentsController : ControllerBase
{
    private readonly ICatalogService _service;
    private readonly IDispatchService _dispatchService;

    public CatalogDocumentsController(ICatalogService service, IDispatchService dispatchService)
    {
        _service = service;
        _dispatchService = dispatchService;
    }

    [HttpGet("{id:guid}/download")]
    [Authorize(Policy = PermissionCodes.CatalogsView)]
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
    /// E9-07: which customers received this document and when, derived from the dispatch log.
    /// A sub-resource endpoint rather than an extra field on <see cref="CatalogDocumentDto"/> —
    /// deliberately, per the coordinator's brief: a frontend agent is concurrently consuming
    /// that DTO's existing shape (E5/E6), so this is additive instead of changing it out from
    /// under them, and "sent to" history is a variable-length, potentially large list that does
    /// not belong on every list-item response the way the DTO's other fields do. Gated on
    /// <see cref="PermissionCodes.CatalogsView"/> (viewing dispatch history about a document is
    /// a Catalogs concern, not a Dispatch.Send concern — a viewer with only Catalogs.View can
    /// see who a document was sent to without being able to send it themselves).
    /// </summary>
    [HttpGet("{id:guid}/dispatches")]
    [Authorize(Policy = PermissionCodes.CatalogsView)]
    public async Task<IActionResult> SentTo(Guid id, CancellationToken ct)
    {
        var result = await _dispatchService.GetDocumentHistoryAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
