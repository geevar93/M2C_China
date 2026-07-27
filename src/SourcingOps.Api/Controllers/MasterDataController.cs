using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Api.Extensions;
using SourcingOps.Application.MasterData;
using SourcingOps.Domain.Constants;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// Binding master-data contract fixed by the coordinator (ACTION_PLAN E3-03…E3-09). Reads
/// require any authenticated user (every feature screen needs lookups); every write requires
/// <see cref="PermissionCodes.AdminManageMasterData"/>.
/// </summary>
[ApiController]
[Route("api/v1/master-data")]
[Authorize]
public sealed class MasterDataController : ControllerBase
{
    private readonly IMasterDataService _service;

    public MasterDataController(IMasterDataService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAggregate([FromQuery] bool includeRetired = false, CancellationToken ct = default)
    {
        var result = await _service.GetAggregateAsync(includeRetired, ct);
        return Ok(result);
    }

    [HttpPost("{collection}")]
    [Authorize(Policy = PermissionCodes.AdminManageMasterData)]
    public async Task<IActionResult> Create(string collection, [FromBody] UpsertMasterDataRequest request, CancellationToken ct)
    {
        if (!MasterDataCollectionKeyExtensions.TryParse(collection, out var key))
        {
            return UnknownCollectionProblem(collection);
        }

        var result = await _service.CreateAsync(key, request, User.GetRequiredUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{collection}/{id:guid}")]
    [Authorize(Policy = PermissionCodes.AdminManageMasterData)]
    public async Task<IActionResult> Update(string collection, Guid id, [FromBody] UpsertMasterDataRequest request, CancellationToken ct)
    {
        if (!MasterDataCollectionKeyExtensions.TryParse(collection, out var key))
        {
            return UnknownCollectionProblem(collection);
        }

        var result = await _service.UpdateAsync(key, id, request, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{collection}/{id:guid}/retire")]
    [Authorize(Policy = PermissionCodes.AdminManageMasterData)]
    public async Task<IActionResult> Retire(string collection, Guid id, CancellationToken ct)
    {
        if (!MasterDataCollectionKeyExtensions.TryParse(collection, out var key))
        {
            return UnknownCollectionProblem(collection);
        }

        var result = await _service.RetireAsync(key, id, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{collection}/{id:guid}/restore")]
    [Authorize(Policy = PermissionCodes.AdminManageMasterData)]
    public async Task<IActionResult> Restore(string collection, Guid id, CancellationToken ct)
    {
        if (!MasterDataCollectionKeyExtensions.TryParse(collection, out var key))
        {
            return UnknownCollectionProblem(collection);
        }

        var result = await _service.RestoreAsync(key, id, User.GetRequiredUserId(), ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{collection}/reorder")]
    [Authorize(Policy = PermissionCodes.AdminManageMasterData)]
    public async Task<IActionResult> Reorder(string collection, [FromBody] List<ReorderItemDto> items, CancellationToken ct)
    {
        if (!MasterDataCollectionKeyExtensions.TryParse(collection, out var key))
        {
            return UnknownCollectionProblem(collection);
        }

        var result = await _service.ReorderAsync(key, items, User.GetRequiredUserId(), ct);
        return Ok(result);
    }

    [HttpDelete("{collection}/{id:guid}")]
    [Authorize(Policy = PermissionCodes.AdminManageMasterData)]
    public async Task<IActionResult> Delete(string collection, Guid id, CancellationToken ct)
    {
        if (!MasterDataCollectionKeyExtensions.TryParse(collection, out var key))
        {
            return UnknownCollectionProblem(collection);
        }

        var result = await _service.DeleteAsync(key, id, User.GetRequiredUserId(), ct);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (!result.Deleted)
        {
            // E3-08's acceptance criterion: 409 ProblemDetails whose detail tells the caller
            // to retire instead — the frontend surfaces this string directly.
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Cannot delete a referenced master-data row.", detail: result.ConflictDetail);
        }

        return NoContent();
    }

    private ObjectResult UnknownCollectionProblem(string collection) =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Unknown master-data collection.", detail: $"'{collection}' is not a recognised master-data collection.");
}
