using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Application.Analytics;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// M7 contract (ACTION_PLAN E10-01…E10-07/E10-10). All six routes are gated on the single
/// existing <see cref="PermissionCodes.AnalyticsView"/> permission and accept the SAME four
/// query parameters (E10-07) — one shared <see cref="AnalyticsQuery"/>, not six near-copies.
/// E10-08 (CSV/Excel export) is out of scope for this pass and intentionally absent here.
/// </summary>
[ApiController]
[Route("api/v1/analytics")]
[Authorize(Policy = PermissionCodes.AnalyticsView)]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _service;

    public AnalyticsController(IAnalyticsService service)
    {
        _service = service;
    }

    [HttpGet("leads")]
    public async Task<IActionResult> Leads(
        [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? categoryId, [FromQuery] Guid? serviceTypeId, CancellationToken ct) =>
        Ok(await _service.GetLeadsAsync(new AnalyticsQuery(fromDate, toDate, categoryId, serviceTypeId), ct));

    [HttpGet("service-split")]
    public async Task<IActionResult> ServiceSplit(
        [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? categoryId, [FromQuery] Guid? serviceTypeId, CancellationToken ct) =>
        Ok(await _service.GetServiceSplitAsync(new AnalyticsQuery(fromDate, toDate, categoryId, serviceTypeId), ct));

    [HttpGet("category-mix")]
    public async Task<IActionResult> CategoryMix(
        [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? categoryId, [FromQuery] Guid? serviceTypeId, CancellationToken ct) =>
        Ok(await _service.GetCategoryMixAsync(new AnalyticsQuery(fromDate, toDate, categoryId, serviceTypeId), ct));

    [HttpGet("vendors")]
    public async Task<IActionResult> Vendors(
        [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? categoryId, [FromQuery] Guid? serviceTypeId, CancellationToken ct) =>
        Ok(await _service.GetVendorsAsync(new AnalyticsQuery(fromDate, toDate, categoryId, serviceTypeId), ct));

    /// <summary>E10-05/E10-10: never cached — see <c>AnalyticsService.GetInventoryAsync</c>'s doc comment.</summary>
    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory(
        [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? categoryId, [FromQuery] Guid? serviceTypeId, CancellationToken ct) =>
        Ok(await _service.GetInventoryAsync(new AnalyticsQuery(fromDate, toDate, categoryId, serviceTypeId), ct));

    [HttpGet("dispatch")]
    public async Task<IActionResult> Dispatch(
        [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? categoryId, [FromQuery] Guid? serviceTypeId, CancellationToken ct) =>
        Ok(await _service.GetDispatchAsync(new AnalyticsQuery(fromDate, toDate, categoryId, serviceTypeId), ct));
}
