using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingOps.Infrastructure.Persistence;

namespace SourcingOps.Api.Controllers;

/// <summary>
/// TECH_SPEC §7.2; ACTION_PLAN E1-17. Reports API liveness and actual database
/// reachability — used by the E2-10 deploy smoke check.
/// </summary>
[ApiController]
[Route("api/v1/health")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    private readonly AppDbContext _db;

    public HealthController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var databaseReachable = false;
        try
        {
            databaseReachable = await _db.Database.CanConnectAsync(ct);
        }
        catch
        {
            databaseReachable = false; // never leak the underlying connection error to the client
        }

        var response = new
        {
            status = databaseReachable ? "Healthy" : "Unhealthy",
            timestampUtc = DateTime.UtcNow,
            checks = new
            {
                api = "Healthy",
                database = databaseReachable ? "Healthy" : "Unhealthy"
            }
        };

        return databaseReachable ? Ok(response) : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}
