using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Api.Extensions;
using SourcingOps.Application.Invoicing;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// M6 contract §2 (ACTION_PLAN E8-01…E8-08). Reads require <see cref="PermissionCodes.InvoicingView"/>,
/// general writes <see cref="PermissionCodes.InvoicingEdit"/>, and marking paid its own
/// <see cref="PermissionCodes.InvoicingMarkPaid"/> — all three already existed in the seeded
/// catalog, so this pass added no permission. Status only ever moves through
/// <see cref="ChangeStatus"/> or <see cref="MarkPaid"/> — never through <see cref="Update"/>
/// (M6 contract §2's two traps). Every mutation writes through <c>IAuditLogger</c> inside
/// <see cref="InvoiceService"/>; invoice-state conflicts (non-Draft edit, illegal transition,
/// PDF not yet available) surface as 409 ProblemDetails from
/// <c>ExceptionHandlingMiddleware</c>'s <c>InvoiceConflictException</c> mapping, not from this
/// controller.
/// </summary>
[ApiController]
[Route("api/v1/invoices")]
[Authorize]
public sealed class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _service;

    public InvoicesController(IInvoiceService service)
    {
        _service = service;
    }

    /// <summary>E8-05: customer/status/service-type/date-range filters, search on invoice number or customer name, paging, plus per-status tab counts.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCodes.InvoicingView)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] Guid? customerId = null,
        [FromQuery] Guid? statusId = null,
        [FromQuery] Guid? serviceTypeId = null,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        CancellationToken ct = default)
    {
        var query = new InvoiceListQuery(search, page, pageSize, customerId, statusId, serviceTypeId, fromDate, toDate);
        var result = await _service.ListAsync(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCodes.InvoicingView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _service.GetAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>E8-01: always created Draft; the invoice number is generated server-side (M6 contract §1).</summary>
    [HttpPost]
    [Authorize(Policy = PermissionCodes.InvoicingEdit)]
    public async Task<IActionResult> Create([FromBody] CreateInvoiceRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, User.GetRequiredUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Editable only in Draft — 409 otherwise (M6 contract §2).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCodes.InvoicingEdit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateInvoiceRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>E8-02/E8-03: the ONLY way status moves besides <see cref="MarkPaid"/>. DRAFT→ISSUED renders and stores the PDF.</summary>
    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = PermissionCodes.InvoicingEdit)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeInvoiceStatusRequest request, CancellationToken ct)
    {
        var result = await _service.ChangeStatusAsync(id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>E8-07: separate endpoint, separate permission (M6 contract §2).</summary>
    [HttpPost("{id:guid}/mark-paid")]
    [Authorize(Policy = PermissionCodes.InvoicingMarkPaid)]
    public async Task<IActionResult> MarkPaid(Guid id, [FromBody] MarkInvoicePaidRequest request, CancellationToken ct)
    {
        var result = await _service.MarkPaidAsync(id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>E8-03: authenticated download, never a static path (TECH_SPEC §8/§4.6).</summary>
    [HttpGet("{id:guid}/pdf")]
    [Authorize(Policy = PermissionCodes.InvoicingView)]
    public async Task<IActionResult> DownloadPdf(Guid id, CancellationToken ct)
    {
        var download = await _service.GetPdfAsync(id, ct);
        if (download is null)
        {
            return NotFound();
        }

        return File(download.Content, "application/pdf", download.FileName);
    }
}
