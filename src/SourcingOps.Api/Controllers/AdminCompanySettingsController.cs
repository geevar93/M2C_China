using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Api.Extensions;
using SourcingOps.Application.Admin;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// M6 contract §0 — converts FSD Q9c from an engineering blocker into a data-entry task. Both
/// routes require <see cref="PermissionCodes.AdminManageMasterData"/>, the existing
/// master-data write permission (M6 contract §2's routing table: "use whatever the existing
/// master-data write permission actually is"), consistent with <c>MasterDataController</c>'s
/// write gate. Reads are gated too (unlike <c>MasterDataController.GetAggregate</c>, which any
/// authenticated user can call) — billing/bank details are sensitive in a way lookup labels are
/// not.
/// </summary>
[ApiController]
[Route("api/v1/admin/company-settings")]
[Authorize(Policy = PermissionCodes.AdminManageMasterData)]
public sealed class AdminCompanySettingsController : ControllerBase
{
    private readonly ICompanySettingsService _service;

    public AdminCompanySettingsController(ICompanySettingsService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await _service.GetAsync(ct);
        return Ok(result);
    }

    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] UpsertCompanySettingsRequest request, CancellationToken ct)
    {
        var result = await _service.UpsertAsync(request, User.GetRequiredUserId(), ct);
        return Ok(result);
    }
}
